/*
  ═══════════════════════════════════════════════════════════════════════════════
   Project Tracker — التثبيت والترقية الكاملة في ملف واحد
  ═══════════════════════════════════════════════════════════════════════════════

   الاستخدام:  افتح الملف في SSMS  →  اضغط F5  →  انتهى.
   يعمل على قاعدة جديدة أو قاعدة موجودة؛ لا تحتاج تشغيل أي migration آخر بعده.

   ينشئ هذا الملف بالترتيب:
     1. قاعدة البيانات  [ProjectTracker]
     2. حساب الاتصال    tracker_user
     3. أربعة عشر جدولًا وكل الأعمدة والقيود والفهارس الحالية
        (+ جدولا إشعارات البريد الإلكتروني: EmailTemplates وEmailNotificationLog — القسم 8-ب)
     4. ترقية القواعد القديمة إلى أحدث مخطط
     5. تحويل كلمات المرور النصية القديمة إلى تجزئة مملّحة
     6. إعدادات اللوحة وحساب مدير النظام الأول
     7. تقرير تحقق في النهاية + بيانات .env.local جاهزة للنسخ

   ✅ آمن لإعادة التشغيل: لا يحذف بيانات العمل. شغّله مرة أو عشرًا.
   ✅ لا يعتمد على إصدار خادم ولا على مسارات جهاز — يعمل على SQL Server 2016 فما فوق.

   ── تريد اسم قاعدة مختلفًا؟ ──────────────────────────────────────────────────
   استبدل  [ProjectTracker]  بالاسم الذي تريد (Ctrl+H في SSMS، ابحث بالأقواس
   المعقوفة). يظهر في موضعين فقط. ثم اضبط DB_NAME في .env.local بالاسم نفسه.

   ── كلمات المرور ────────────────────────────────────────────────────────────
   ⚠ الملف يحمل كلمتَي مرور افتراضيتين ليعمل بلا تدخّل. غيّرهما بعد أول دخول:
     • كلمة حساب SQL   → من SSMS
     • كلمة مدير النظام → من داخل النظام
   وإن أردت تغييرهما قبل التشغيل فهما في القسمين (2) و(9) أدناه، مكتوبتان بوضوح.
  ═══════════════════════════════════════════════════════════════════════════════
*/

SET NOCOUNT ON;
GO

-- جدول مؤقت يحمل حالة التثبيت بين الدفعات (يعيش ما دامت الجلسة مفتوحة).
-- الغرض: أن يعرف التقرير النهائي هل أُنشئ حساب SQL في هذه الجلسة فعلًا — فإن كان
-- موجودًا من قبل فكلمة المرور أدناه ليست كلمته، ولا يصح طباعتها كأنها صحيحة.
IF OBJECT_ID(N'tempdb..#state') IS NOT NULL DROP TABLE #state;
CREATE TABLE #state (loginCreated BIT NOT NULL DEFAULT 0, loginFailed BIT NOT NULL DEFAULT 0);
INSERT INTO #state (loginCreated, loginFailed) VALUES (0, 0);
GO

PRINT N'';
PRINT N'═══════════════════════════════════════════════════════════';
PRINT N'  Project Tracker — بدء التثبيت';
PRINT N'═══════════════════════════════════════════════════════════';
GO


/* ═════════════ (1) قاعدة البيانات ═════════════
   بلا FILENAME (يضعها الخادم في مجلده الافتراضي)، وبلا COMPATIBILITY_LEVEL
   (تأخذ افتراضي الخادم)، وبلا LEDGER / OPTIMIZED_LOCKING المقيّدة بإصدار.   */

USE [master];
GO

IF DB_ID(N'ProjectTracker') IS NULL
BEGIN
  CREATE DATABASE [ProjectTracker];
  PRINT N'  ✓ (1) أُنشئت القاعدة [ProjectTracker]';
END
ELSE
  PRINT N'  • (1) القاعدة [ProjectTracker] موجودة — لم تُنشأ من جديد.';
GO

-- AUTO_CLOSE OFF مقصود: تُغلق القاعدة عند خلوّها من الاتصالات فيدفع أول طلب بعد
-- فترة هدوء ثمن إعادة الفتح (مهلة اتصال متقطّعة في التطبيق).
-- وإعدادات ANSI الستة لازمة للفهارس المُصفّاة على profiles.
IF DB_ID(N'ProjectTracker') IS NOT NULL
BEGIN
  ALTER DATABASE [ProjectTracker] SET AUTO_CLOSE OFF;
  ALTER DATABASE [ProjectTracker] SET RECOVERY SIMPLE;
  ALTER DATABASE [ProjectTracker] SET ANSI_NULLS ON;
  ALTER DATABASE [ProjectTracker] SET QUOTED_IDENTIFIER ON;
  ALTER DATABASE [ProjectTracker] SET ANSI_PADDING ON;
  ALTER DATABASE [ProjectTracker] SET ANSI_WARNINGS ON;
  ALTER DATABASE [ProjectTracker] SET ARITHABORT ON;
  ALTER DATABASE [ProjectTracker] SET CONCAT_NULL_YIELDS_NULL ON;
  PRINT N'  ✓ (1) ضُبطت إعدادات القاعدة.';
END
GO


/* ═════════════ (2) حساب الاتصال على الخادم ═════════════ */

-- ⚠ كلمة مرور حساب SQL — غيّرها هنا إن أردت، أو من SSMS بعد التثبيت.
DECLARE @loginPassword NVARCHAR(128) = N'ChangeMe#Sql2026!';

BEGIN TRY
  IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'tracker_user')
  BEGIN
    DECLARE @sql NVARCHAR(MAX) =
      N'CREATE LOGIN [tracker_user] WITH PASSWORD = ' + QUOTENAME(@loginPassword, '''')
    + N', DEFAULT_DATABASE = [ProjectTracker], CHECK_POLICY = ON;';
    EXEC sys.sp_executesql @sql;
    UPDATE #state SET loginCreated = 1;
    PRINT N'  ✓ (2) أُنشئ حساب الخادم tracker_user';
  END
  ELSE
    PRINT N'  • (2) حساب الخادم tracker_user موجود — كلمة مروره لم تُغيَّر.';
END TRY
BEGIN CATCH
  -- لا نُسقط التثبيت بسبب الحساب: المخطط أهم، والحساب يمكن إنشاؤه يدويًا لاحقًا.
  -- يقع هذا غالبًا إن كان الخادم بمصادقة ويندوز فقط، أو كلمة المرور دون سياسة الخادم.
  UPDATE #state SET loginFailed = 1;
  PRINT N'  ⚠ (2) تعذّر إنشاء حساب tracker_user: ' + ERROR_MESSAGE();
  PRINT N'      التثبيت مستمر. أنشئ الحساب يدويًا، أو استخدم مصادقة ويندوز في .env.local';
END CATCH
GO


/* ═════════════ (3) الانتقال إلى القاعدة + الحارس ═════════════
   هذا الحارس هو الفرق الجوهري عن الملف القديم: إن لم تُنشأ القاعدة لأي سبب،
   يبقى السياق على master ويُكمل SSMS بقية الدفعات فيها. SET NOEXEC ON توقف
   كل ما بعدها بلا استثناء، فلا يُكتب حرف واحد في قاعدة النظام.               */

USE [ProjectTracker];
GO

IF DB_NAME() <> N'ProjectTracker'
BEGIN
  PRINT N'';
  PRINT N'  ✗ فشل الانتقال إلى القاعدة [ProjectTracker] — السياق الحالي: ' + DB_NAME();
  PRINT N'    راجع أخطاء القسم (1) أعلاه. لم يُنفَّذ أي شيء بعد هذه النقطة.';
  SET NOEXEC ON;
END
GO

-- إعدادات الجلسة. لازمة للفهارس المُصفّاة على profiles: أي INSERT/UPDATE/SELECT
-- عليها بإعداد خاطئ يُرفض بالخطأ 1934 عند **تصريف الدفعة** — فتسقط الدفعة كلها
-- قبل تنفيذ أي سطر منها، حتى الحُرّاس لا تعمل ولا تظهر رسائلها.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

PRINT N'  ✓ (3) السياق الآن: ' + DB_NAME();
GO


/* ═════════════ (4) ربط حساب الاتصال بالقاعدة ═════════════
   db_owner لأن التطبيق وسكربتات الترقية تحتاج صلاحية DDL.
   للإنتاج بعد استقرار المخطط: اخفضه إلى db_datareader + db_datawriter.        */

BEGIN TRY
  IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'tracker_user')
  BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'tracker_user')
      CREATE USER [tracker_user] FOR LOGIN [tracker_user] WITH DEFAULT_SCHEMA = [dbo];
    ALTER ROLE [db_owner] ADD MEMBER [tracker_user];
    PRINT N'  ✓ (4) رُبط tracker_user بالقاعدة بدور db_owner.';
  END
  ELSE
    PRINT N'  ⚠ (4) لا يوجد حساب tracker_user على الخادم — تُخطّي الربط.';
END TRY
BEGIN CATCH
  PRINT N'  ⚠ (4) تعذّر ربط tracker_user: ' + ERROR_MESSAGE();
END CATCH
GO


/* ═══════════════════════════════════════════════════════════════════
   (5) الجداول الأربعة عشر
   كل قيد وكل فهرس باسم صريح — فإعادة التشغيل لا تُنشئ نسخة ثانية منه.
   (الملف القديم كان يضيف قيودًا بلا أسماء فتكرّرت ثلاث مرات، وتكرّر
    مفتاح CASCADE فظهر الخطأ 1785 «multiple cascade paths».)
   ═══════════════════════════════════════════════════════════════════ */

/* ── 1) profiles — الموظفون والحسابات ── */
IF OBJECT_ID(N'dbo.profiles', N'U') IS NULL
CREATE TABLE dbo.profiles (
  id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_profiles_id         DEFAULT NEWID(),
  full_name       NVARCHAR(100)    NOT NULL,
  email           NVARCHAR(150)    NULL,
  role            NVARCHAR(20)     NULL     CONSTRAINT DF_profiles_role       DEFAULT 'pm',
  department      NVARCHAR(100)    NULL,
  -- تجزئة ذات إصدار: scrypt للتطبيق أو sha512 مملّح للترقية داخل SQL.
  password_hash   NVARCHAR(255)    NULL,
  created_at      DATETIME2(7)     NULL     CONSTRAINT DF_profiles_created_at DEFAULT GETDATE(),
  status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_profiles_status     DEFAULT 'active',
  -- بيانات الموظف
  employee_number           NVARCHAR(50)  NULL,
  job_title                 NVARCHAR(200) NULL,
  mobile_no                 NVARCHAR(30)  NULL,
  main_department           NVARCHAR(200) NULL,
  section_name              NVARCHAR(200) NULL,
  extension_number          NVARCHAR(20)  NULL,
  manager_name              NVARCHAR(200) NULL,
  manager_number            NVARCHAR(50)  NULL,
  authorized_manager_name   NVARCHAR(200) NULL,
  authorized_manager_number NVARCHAR(50)  NULL,
  directory_synced_at       DATETIME2     NULL,
  CONSTRAINT PK_profiles PRIMARY KEY CLUSTERED (id),
  -- الأدوار الثلاثة تطابق USER_ROLES في lib/userRules.ts.
  CONSTRAINT CK_profiles_role CHECK (role IN ('admin', 'deputy', 'pm'))
);
GO

/* ── 2) projects — المشاريع ── */
IF OBJECT_ID(N'dbo.projects', N'U') IS NULL
CREATE TABLE dbo.projects (
  id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_projects_id          DEFAULT NEWID(),
  name         NVARCHAR(200)    NOT NULL,
  description  NVARCHAR(MAX)    NULL,
  start_date   DATE             NULL,
  end_date     DATE             NULL,
  total_weeks  INT              NULL     CONSTRAINT DF_projects_total_weeks DEFAULT 14,
  status       NVARCHAR(20)     NULL     CONSTRAINT DF_projects_status      DEFAULT 'active',
  created_at   DATETIME2(7)     NULL     CONSTRAINT DF_projects_created_at  DEFAULT GETDATE(),
  project_code NVARCHAR(50)     NULL,
  department   NVARCHAR(200)    NULL,
  priority     NVARCHAR(20)     NULL,
  CONSTRAINT PK_projects PRIMARY KEY CLUSTERED (id),
  CONSTRAINT CK_projects_priority CHECK (priority IS NULL OR priority IN (N'high', N'medium', N'low'))
);
GO

/* ── 3) milestones — المراحل ── */
IF OBJECT_ID(N'dbo.milestones', N'U') IS NULL
CREATE TABLE dbo.milestones (
  id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_milestones_id            DEFAULT NEWID(),
  project_id    UNIQUEIDENTIFIER NULL,
  name          NVARCHAR(200)    NOT NULL,
  week_start    INT              NOT NULL,
  week_end      INT              NOT NULL,
  color         NVARCHAR(20)     NULL     CONSTRAINT DF_milestones_color         DEFAULT '#255aa0',
  display_order INT              NULL     CONSTRAINT DF_milestones_display_order DEFAULT 0,
  CONSTRAINT PK_milestones PRIMARY KEY CLUSTERED (id),
  CONSTRAINT FK_milestones_project FOREIGN KEY (project_id)
    REFERENCES dbo.projects (id) ON DELETE CASCADE
);
GO

/* ── 4) tasks — المهام ── */
IF OBJECT_ID(N'dbo.tasks', N'U') IS NULL
CREATE TABLE dbo.tasks (
  id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_tasks_id         DEFAULT NEWID(),
  milestone_id UNIQUEIDENTIFIER NULL,
  name         NVARCHAR(200)    NOT NULL,
  week_start   INT              NOT NULL,
  week_end     INT              NOT NULL,
  progress     INT              NULL     CONSTRAINT DF_tasks_progress    DEFAULT 0,
  status       NVARCHAR(20)     NULL     CONSTRAINT DF_tasks_status      DEFAULT 'pending',
  updated_at   DATETIME2(7)     NULL     CONSTRAINT DF_tasks_updated_at  DEFAULT GETDATE(),
  CONSTRAINT PK_tasks PRIMARY KEY CLUSTERED (id),
  CONSTRAINT FK_tasks_milestone FOREIGN KEY (milestone_id)
    REFERENCES dbo.milestones (id) ON DELETE CASCADE,
  CONSTRAINT CK_tasks_progress CHECK (progress >= 0 AND progress <= 100)
);
GO

/* ── 5) phase_notes — تفاصيل المراحل وطلبات الدعم ── */
IF OBJECT_ID(N'dbo.phase_notes', N'U') IS NULL
CREATE TABLE dbo.phase_notes (
  id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_phase_notes_id            DEFAULT NEWID(),
  milestone_id  UNIQUEIDENTIFIER NULL,
  type          NVARCHAR(10)     NULL,
  content       NVARCHAR(MAX)    NOT NULL,
  display_order INT              NULL     CONSTRAINT DF_phase_notes_display_order DEFAULT 0,
  CONSTRAINT PK_phase_notes PRIMARY KEY CLUSTERED (id),
  CONSTRAINT FK_phase_notes_milestone FOREIGN KEY (milestone_id)
    REFERENCES dbo.milestones (id) ON DELETE CASCADE,
  CONSTRAINT CK_phase_notes_type CHECK (type IS NULL OR type IN ('detail', 'support'))
);
GO

/* ── 6) assignments — إسناد الموظفين للمشاريع ── */
IF OBJECT_ID(N'dbo.assignments', N'U') IS NULL
CREATE TABLE dbo.assignments (
  id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_assignments_id          DEFAULT NEWID(),
  project_id      UNIQUEIDENTIFIER NULL,
  user_id         UNIQUEIDENTIFIER NULL,
  role_in_project NVARCHAR(50)     NULL     CONSTRAINT DF_assignments_role        DEFAULT 'pm',
  assigned_at     DATETIME2(7)     NULL     CONSTRAINT DF_assignments_assigned_at DEFAULT GETDATE(),
  CONSTRAINT PK_assignments PRIMARY KEY CLUSTERED (id),
  CONSTRAINT UQ_assignments_project_user UNIQUE (project_id, user_id),
  CONSTRAINT FK_assignments_project FOREIGN KEY (project_id)
    REFERENCES dbo.projects (id) ON DELETE CASCADE,
  CONSTRAINT FK_assignments_user FOREIGN KEY (user_id)
    REFERENCES dbo.profiles (id) ON DELETE CASCADE
);
GO

/* ── 7) project_updates — التحديثات الأسبوعية (إدراج فقط) ── */
IF OBJECT_ID(N'dbo.project_updates', N'U') IS NULL
CREATE TABLE dbo.project_updates (
  id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_project_updates_id         DEFAULT NEWID(),
  project_id           UNIQUEIDENTIFIER NOT NULL,
  reporting_date       DATE             NOT NULL,
  executive_summary    NVARCHAR(MAX)    NULL,
  actual_progress      INT              NULL,
  planned_progress     INT              NULL,
  variance_percent     INT              NULL,
  health_status        NVARCHAR(20)     NOT NULL,
  -- بلا FK إلى milestones عمدًا: المراحل تُحذف فعليًا من /pm، وFK كان سيمنع حذف
  -- أي مرحلة ذُكرت في تحديث قديم. الخادم يتحقق من انتماء المرحلة وقت الكتابة.
  current_milestone_id UNIQUEIDENTIFIER NULL,
  next_milestone_id    UNIQUEIDENTIFIER NULL,
  forecast_end_date    DATE             NULL,
  delay_reason         NVARCHAR(MAX)    NULL,
  support_required     NVARCHAR(MAX)    NULL,
  support_from         NVARCHAR(200)    NULL,
  support_impact       NVARCHAR(MAX)    NULL,
  support_due_date     DATE             NULL,
  decision_required    NVARCHAR(MAX)    NULL,
  decision_from        NVARCHAR(200)    NULL,
  decision_needed_by   DATE             NULL,
  decision_status      NVARCHAR(20)     NULL,
  updated_by           UNIQUEIDENTIFIER NULL,
  created_at           DATETIME2        NOT NULL CONSTRAINT DF_project_updates_created_at DEFAULT GETDATE(),
  CONSTRAINT PK_project_updates PRIMARY KEY (id),
  CONSTRAINT FK_project_updates_project    FOREIGN KEY (project_id) REFERENCES dbo.projects (id),
  CONSTRAINT FK_project_updates_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.profiles (id),
  CONSTRAINT CK_project_updates_actual  CHECK (actual_progress  IS NULL OR actual_progress  BETWEEN 0 AND 100),
  CONSTRAINT CK_project_updates_planned CHECK (planned_progress IS NULL OR planned_progress BETWEEN 0 AND 100),
  CONSTRAINT CK_project_updates_health  CHECK (health_status IN (N'on_track', N'needs_attention', N'delayed', N'at_risk', N'stopped')),
  CONSTRAINT CK_project_updates_decision_status CHECK (decision_status IS NULL OR decision_status IN (N'new', N'under_review', N'taken'))
);
GO

/* ── 8) risks_issues — المخاطر والتعثرات والعوائق ── */
IF OBJECT_ID(N'dbo.risks_issues', N'U') IS NULL
CREATE TABLE dbo.risks_issues (
  id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_risks_issues_id         DEFAULT NEWID(),
  project_id   UNIQUEIDENTIFIER NOT NULL,
  milestone_id UNIQUEIDENTIFIER NULL,
  type         NVARCHAR(20)     NOT NULL,
  title        NVARCHAR(300)    NOT NULL,
  description  NVARCHAR(MAX)    NULL,
  severity     NVARCHAR(20)     NOT NULL,
  probability  NVARCHAR(20)     NULL,
  status       NVARCHAR(20)     NOT NULL CONSTRAINT DF_risks_issues_status     DEFAULT N'open',
  owner_id     UNIQUEIDENTIFIER NULL,
  due_date     DATE             NULL,
  mitigation   NVARCHAR(MAX)    NULL,
  created_by   UNIQUEIDENTIFIER NULL,
  created_at   DATETIME2        NOT NULL CONSTRAINT DF_risks_issues_created_at DEFAULT GETDATE(),
  updated_at   DATETIME2        NOT NULL CONSTRAINT DF_risks_issues_updated_at DEFAULT GETDATE(),
  CONSTRAINT PK_risks_issues PRIMARY KEY (id),
  CONSTRAINT FK_risks_issues_project FOREIGN KEY (project_id) REFERENCES dbo.projects (id),
  CONSTRAINT FK_risks_issues_owner   FOREIGN KEY (owner_id)   REFERENCES dbo.profiles (id),
  CONSTRAINT CK_risks_issues_type        CHECK (type IN (N'risk', N'issue', N'blocker')),
  CONSTRAINT CK_risks_issues_severity    CHECK (severity IN (N'low', N'medium', N'high', N'critical')),
  CONSTRAINT CK_risks_issues_probability CHECK (probability IS NULL OR probability IN (N'low', N'medium', N'high')),
  CONSTRAINT CK_risks_issues_status      CHECK (status IN (N'open', N'mitigating', N'closed'))
);
GO

/* ── 9) meeting_actions — قرارات وإجراءات الاجتماع ── */
IF OBJECT_ID(N'dbo.meeting_actions', N'U') IS NULL
CREATE TABLE dbo.meeting_actions (
  id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_meeting_actions_id         DEFAULT NEWID(),
  project_id            UNIQUEIDENTIFIER NOT NULL,
  meeting_date          DATE             NOT NULL,
  action_type           NVARCHAR(20)     NOT NULL,
  description           NVARCHAR(MAX)    NOT NULL,
  owner_id              UNIQUEIDENTIFIER NULL,
  supporting_department NVARCHAR(200)    NULL,
  due_date              DATE             NULL,
  status                NVARCHAR(20)     NOT NULL CONSTRAINT DF_meeting_actions_status     DEFAULT N'new',
  decision_notes        NVARCHAR(MAX)    NULL,
  closure_notes         NVARCHAR(MAX)    NULL,
  created_by            UNIQUEIDENTIFIER NULL,
  created_at            DATETIME2        NOT NULL CONSTRAINT DF_meeting_actions_created_at DEFAULT GETDATE(),
  updated_at            DATETIME2        NOT NULL CONSTRAINT DF_meeting_actions_updated_at DEFAULT GETDATE(),
  CONSTRAINT PK_meeting_actions PRIMARY KEY (id),
  CONSTRAINT FK_meeting_actions_project FOREIGN KEY (project_id) REFERENCES dbo.projects (id),
  CONSTRAINT FK_meeting_actions_owner   FOREIGN KEY (owner_id)   REFERENCES dbo.profiles (id),
  CONSTRAINT CK_meeting_actions_type   CHECK (action_type IN (N'decision', N'support', N'escalation', N'corrective')),
  CONSTRAINT CK_meeting_actions_status CHECK (status IN (N'new', N'in_progress', N'completed', N'cancelled'))
);
GO

/* ── 10) audit_log — سجل التدقيق (بلا FK عمدًا: يبقى بعد حذف المستخدم أو المشروع) ── */
IF OBJECT_ID(N'dbo.audit_log', N'U') IS NULL
CREATE TABLE dbo.audit_log (
  id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_audit_log_id         DEFAULT NEWID(),
  user_id     UNIQUEIDENTIFIER NULL,
  action      NVARCHAR(20)     NOT NULL,
  entity_type NVARCHAR(50)     NOT NULL,
  entity_id   UNIQUEIDENTIFIER NULL,
  project_id  UNIQUEIDENTIFIER NULL,
  details     NVARCHAR(MAX)    NULL,
  created_at  DATETIME2        NOT NULL CONSTRAINT DF_audit_log_created_at DEFAULT GETDATE(),
  CONSTRAINT PK_audit_log PRIMARY KEY (id)
);
GO

/* ── 11) app_settings — إعدادات اللوحة ── */
IF OBJECT_ID(N'dbo.app_settings', N'U') IS NULL
CREATE TABLE dbo.app_settings (
  setting_key   NVARCHAR(100)    NOT NULL,
  setting_value NVARCHAR(400)    NOT NULL,
  updated_by    UNIQUEIDENTIFIER NULL,
  updated_at    DATETIME2        NOT NULL CONSTRAINT DF_app_settings_updated_at DEFAULT GETDATE(),
  CONSTRAINT PK_app_settings PRIMARY KEY (setting_key)
);
GO

/* ── 12) user_sessions — الجلسات القابلة للإلغاء ──
   بدون هذا الجدول يُرفض كل تسجيل دخول (سلوك فاشل-مغلق في proxy.ts).           */
IF OBJECT_ID(N'dbo.user_sessions', N'U') IS NULL
CREATE TABLE dbo.user_sessions (
  id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_user_sessions_id      DEFAULT NEWID(),
  user_id    UNIQUEIDENTIFIER NOT NULL,
  created_at DATETIME2        NOT NULL CONSTRAINT DF_user_sessions_created DEFAULT SYSUTCDATETIME(),
  expires_at DATETIME2        NOT NULL,
  revoked_at DATETIME2        NULL,
  CONSTRAINT PK_user_sessions PRIMARY KEY (id),
  CONSTRAINT FK_user_sessions_profiles FOREIGN KEY (user_id)
    REFERENCES dbo.profiles (id) ON DELETE CASCADE
);
GO

/* ── 13) login_otp — رمز التحقق عند الدخول ── */
IF OBJECT_ID(N'dbo.login_otp', N'U') IS NULL
CREATE TABLE dbo.login_otp (
  id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_login_otp_id        DEFAULT NEWID(),
  user_id      UNIQUEIDENTIFIER NOT NULL,
  -- تجزئة الرمز (SHA-256 = 32 بايت). لا يُخزَّن الرمز نفسه إطلاقًا.
  code_hash    VARBINARY(32)    NOT NULL,
  mobile_tail  NVARCHAR(4)      NULL,
  via          NVARCHAR(20)     NOT NULL,
  created_at   DATETIME2        NOT NULL CONSTRAINT DF_login_otp_created   DEFAULT SYSUTCDATETIME(),
  expires_at   DATETIME2        NOT NULL,
  attempts     INT              NOT NULL CONSTRAINT DF_login_otp_attempts  DEFAULT 0,
  resends      INT              NOT NULL CONSTRAINT DF_login_otp_resends   DEFAULT 0,
  last_sent_at DATETIME2        NOT NULL CONSTRAINT DF_login_otp_last_sent DEFAULT SYSUTCDATETIME(),
  consumed_at  DATETIME2        NULL,
  CONSTRAINT PK_login_otp PRIMARY KEY (id),
  CONSTRAINT FK_login_otp_profiles FOREIGN KEY (user_id)
    REFERENCES dbo.profiles (id) ON DELETE CASCADE
);
GO

/* ── 14) notification_log — منع تكرار الإشعارات ── */
IF OBJECT_ID(N'dbo.notification_log', N'U') IS NULL
CREATE TABLE dbo.notification_log (
  id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_notification_log_id   DEFAULT NEWID(),
  user_id UNIQUEIDENTIFIER NOT NULL,
  -- assignment | weekly | milestone_delay
  kind    NVARCHAR(40)     NOT NULL,
  ref     NVARCHAR(200)    NOT NULL,
  sent_at DATETIME2        NOT NULL CONSTRAINT DF_notification_log_sent DEFAULT SYSUTCDATETIME(),
  detail  NVARCHAR(400)    NULL,
  CONSTRAINT PK_notification_log PRIMARY KEY (id),
  -- القيد الفريد هو آلية منع التكرار نفسها: المرسل يحجز الصف قبل الإرسال،
  -- فتصادُف مُشغِّلَين يُسقط أحدهما بالخطأ 2627 بدل إرسال رسالتين.
  CONSTRAINT UQ_notification_log_once UNIQUE (user_id, kind, ref),
  CONSTRAINT FK_notification_log_profiles FOREIGN KEY (user_id)
    REFERENCES dbo.profiles (id) ON DELETE CASCADE
);
GO

PRINT N'  ✓ (5) الجداول جاهزة.';
GO


/* ═════════════ (6) ترقية القواعد القديمة ═════════════
   كل خطوة مشروطة، لذلك تعمل على قاعدة جديدة أو على أي إصدار سابق. */

IF COL_LENGTH(N'dbo.projects', N'project_code') IS NULL
  ALTER TABLE dbo.projects ADD project_code NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.projects', N'department') IS NULL
  ALTER TABLE dbo.projects ADD department NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.projects', N'priority') IS NULL
  ALTER TABLE dbo.projects ADD priority NVARCHAR(20) NULL;
GO

IF OBJECT_ID(N'dbo.CK_projects_priority', N'C') IS NULL
BEGIN
  ALTER TABLE dbo.projects WITH CHECK
    ADD CONSTRAINT CK_projects_priority
      CHECK (priority IS NULL OR priority IN (N'high', N'medium', N'low'));
END
GO

IF COL_LENGTH(N'dbo.profiles', N'employee_number') IS NULL
  ALTER TABLE dbo.profiles ADD employee_number NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.profiles', N'job_title') IS NULL
  ALTER TABLE dbo.profiles ADD job_title NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.profiles', N'mobile_no') IS NULL
  ALTER TABLE dbo.profiles ADD mobile_no NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.profiles', N'main_department') IS NULL
  ALTER TABLE dbo.profiles ADD main_department NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.profiles', N'section_name') IS NULL
  ALTER TABLE dbo.profiles ADD section_name NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.profiles', N'extension_number') IS NULL
  ALTER TABLE dbo.profiles ADD extension_number NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.profiles', N'manager_name') IS NULL
  ALTER TABLE dbo.profiles ADD manager_name NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.profiles', N'manager_number') IS NULL
  ALTER TABLE dbo.profiles ADD manager_number NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.profiles', N'authorized_manager_name') IS NULL
  ALTER TABLE dbo.profiles ADD authorized_manager_name NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.profiles', N'authorized_manager_number') IS NULL
  ALTER TABLE dbo.profiles ADD authorized_manager_number NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.profiles', N'directory_synced_at') IS NULL
  ALTER TABLE dbo.profiles ADD directory_synced_at DATETIME2 NULL;
GO

-- توحيد قيد الأدوار القديم بحيث يقبل الأدوار الثلاثة المعتمدة.
IF EXISTS (
  SELECT 1 FROM dbo.profiles
  WHERE role IS NOT NULL AND role NOT IN ('admin', 'deputy', 'pm')
)
  THROW 51001, N'توجد أدوار غير معتمدة في profiles. صحّحها إلى admin/deputy/pm ثم أعد التشغيل.', 1;
GO

DECLARE @dropRoleChecks NVARCHAR(MAX) = N'';
SELECT @dropRoleChecks = @dropRoleChecks
  + N'ALTER TABLE dbo.profiles DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(13)
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.profiles')
  AND cc.definition LIKE N'%[[]role]%';

IF @dropRoleChecks <> N'' EXEC sys.sp_executesql @dropRoleChecks;

ALTER TABLE dbo.profiles WITH CHECK
  ADD CONSTRAINT CK_profiles_role CHECK (role IN ('admin', 'deputy', 'pm'));
GO

-- إبقاء قيد فريد واحد فقط على (project_id, user_id) من القواعد القديمة.
DECLARE @keepAssignmentUq SYSNAME = NULL;
DECLARE @dropAssignmentUq SYSNAME;
DECLARE @dropAssignmentSql NVARCHAR(MAX);

SELECT TOP 1 @keepAssignmentUq = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID(N'dbo.assignments')
  AND kc.type = 'UQ'
  AND (SELECT COUNT(*) FROM sys.index_columns ic
       WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id) = 2
  AND NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
      AND c.name NOT IN (N'project_id', N'user_id'))
ORDER BY CASE WHEN kc.name = N'UQ_assignments_project_user' THEN 0 ELSE 1 END, kc.create_date;

DECLARE duplicateAssignmentUq CURSOR LOCAL FAST_FORWARD FOR
  SELECT kc.name
  FROM sys.key_constraints kc
  WHERE kc.parent_object_id = OBJECT_ID(N'dbo.assignments')
    AND kc.type = 'UQ'
    AND kc.name <> @keepAssignmentUq
    AND (SELECT COUNT(*) FROM sys.index_columns ic
         WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id) = 2
    AND NOT EXISTS (
      SELECT 1 FROM sys.index_columns ic
      JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
      WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
        AND c.name NOT IN (N'project_id', N'user_id'));

OPEN duplicateAssignmentUq;
FETCH NEXT FROM duplicateAssignmentUq INTO @dropAssignmentUq;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @dropAssignmentSql = N'ALTER TABLE dbo.assignments DROP CONSTRAINT '
    + QUOTENAME(@dropAssignmentUq) + N';';
  EXEC sys.sp_executesql @dropAssignmentSql;
  FETCH NEXT FROM duplicateAssignmentUq INTO @dropAssignmentUq;
END
CLOSE duplicateAssignmentUq;
DEALLOCATE duplicateAssignmentUq;

IF @keepAssignmentUq IS NULL
  ALTER TABLE dbo.assignments
    ADD CONSTRAINT UQ_assignments_project_user UNIQUE (project_id, user_id);
ELSE IF @keepAssignmentUq <> N'UQ_assignments_project_user'
  EXEC sys.sp_rename @objname = @keepAssignmentUq,
                     @newname = N'UQ_assignments_project_user',
                     @objtype = 'OBJECT';
GO

PRINT N'  ✓ (6) ترقية المخطط القديم اكتملت.';
GO


/* ═════════════ (7) الفهارس ═════════════ */

IF EXISTS (
  SELECT 1 FROM dbo.profiles WHERE employee_number IS NOT NULL
  GROUP BY employee_number HAVING COUNT(*) > 1
)
  PRINT N'  ⚠ تعذّر إنشاء UX_profiles_employee_number: توجد أرقام وظيفية مكررة.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_profiles_employee_number' AND object_id = OBJECT_ID(N'dbo.profiles'))
  CREATE UNIQUE NONCLUSTERED INDEX UX_profiles_employee_number
    ON dbo.profiles (employee_number) WHERE employee_number IS NOT NULL;
GO
-- لا نوقف الترقية إن كانت القاعدة القديمة تحتوي بريدًا مكررًا؛ يظهر تحذير واضح لإصلاحه.
IF EXISTS (
  SELECT 1
  FROM dbo.profiles
  WHERE email IS NOT NULL AND status <> 'deleted'
  GROUP BY LOWER(LTRIM(RTRIM(email)))
  HAVING COUNT(*) > 1
)
  PRINT N'  ⚠ تعذّر إنشاء UX_profiles_email_active: توجد عناوين بريد نشطة مكررة.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_profiles_email_active' AND object_id = OBJECT_ID(N'dbo.profiles'))
  CREATE UNIQUE NONCLUSTERED INDEX UX_profiles_email_active
    ON dbo.profiles (email) WHERE email IS NOT NULL AND status <> 'deleted';
GO
-- Note (2026-09-20): فهارس المفاتيح الأجنبية — SQL Server لا يُنشئها تلقائيًا، وهذه الأعمدة
-- الأربعة يبحث بها النظام في كل شاشة. قياس على 300 مشروع / 36,000 مهمة: /api/milestones أسرع
-- 7.1 مرة، /api/tasks 2.8 مرة، /api/notes 3.4 مرة.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_milestones_project' AND object_id = OBJECT_ID(N'dbo.milestones'))
  CREATE NONCLUSTERED INDEX IX_milestones_project
    ON dbo.milestones (project_id) INCLUDE (week_start, display_order, name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tasks_milestone' AND object_id = OBJECT_ID(N'dbo.tasks'))
  CREATE NONCLUSTERED INDEX IX_tasks_milestone
    ON dbo.tasks (milestone_id) INCLUDE (progress, week_start);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_phase_notes_milestone' AND object_id = OBJECT_ID(N'dbo.phase_notes'))
  CREATE NONCLUSTERED INDEX IX_phase_notes_milestone
    ON dbo.phase_notes (milestone_id) INCLUDE (display_order);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_assignments_user' AND object_id = OBJECT_ID(N'dbo.assignments'))
  CREATE NONCLUSTERED INDEX IX_assignments_user
    ON dbo.assignments (user_id) INCLUDE (project_id, role_in_project);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_project_updates_project_created' AND object_id = OBJECT_ID(N'dbo.project_updates'))
  CREATE NONCLUSTERED INDEX IX_project_updates_project_created
    ON dbo.project_updates (project_id, created_at DESC) INCLUDE (reporting_date);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_risks_issues_project_status' AND object_id = OBJECT_ID(N'dbo.risks_issues'))
  CREATE NONCLUSTERED INDEX IX_risks_issues_project_status
    ON dbo.risks_issues (project_id, status) INCLUDE (type, severity);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_meeting_actions_project_status' AND object_id = OBJECT_ID(N'dbo.meeting_actions'))
  CREATE NONCLUSTERED INDEX IX_meeting_actions_project_status
    ON dbo.meeting_actions (project_id, status) INCLUDE (action_type, due_date);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_meeting_actions_status_due' AND object_id = OBJECT_ID(N'dbo.meeting_actions'))
  CREATE NONCLUSTERED INDEX IX_meeting_actions_status_due
    ON dbo.meeting_actions (status, due_date) INCLUDE (project_id, owner_id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_audit_log_created' AND object_id = OBJECT_ID(N'dbo.audit_log'))
  CREATE NONCLUSTERED INDEX IX_audit_log_created ON dbo.audit_log (created_at DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_audit_log_project_created' AND object_id = OBJECT_ID(N'dbo.audit_log'))
  CREATE NONCLUSTERED INDEX IX_audit_log_project_created ON dbo.audit_log (project_id, created_at DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_user_sessions_user' AND object_id = OBJECT_ID(N'dbo.user_sessions'))
  CREATE NONCLUSTERED INDEX IX_user_sessions_user ON dbo.user_sessions (user_id) INCLUDE (revoked_at, expires_at);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_login_otp_user' AND object_id = OBJECT_ID(N'dbo.login_otp'))
  CREATE NONCLUSTERED INDEX IX_login_otp_user ON dbo.login_otp (user_id) INCLUDE (expires_at, consumed_at);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_login_otp_expires' AND object_id = OBJECT_ID(N'dbo.login_otp'))
  CREATE NONCLUSTERED INDEX IX_login_otp_expires ON dbo.login_otp (expires_at);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notification_log_sent' AND object_id = OBJECT_ID(N'dbo.notification_log'))
  CREATE NONCLUSTERED INDEX IX_notification_log_sent ON dbo.notification_log (sent_at);
GO

PRINT N'  ✓ (7) الفهارس جاهزة.';
GO


/* ═════════════ (8) إعدادات اللوحة الافتراضية ═════════════
   إعدادات لا بيانات عرض: عتبات حساب حالة المشروع في لوحة القيادة التنفيذية.   */

IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'stale_update_days')
  INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'stale_update_days', N'14');
IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'variance_on_track')
  INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'variance_on_track', N'-5');
IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'variance_attention')
  INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'variance_attention', N'-15');
IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'near_deadline_days')
  INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'near_deadline_days', N'30');
GO

PRINT N'  ✓ (8) إعدادات اللوحة جاهزة.';
GO


/* ═════════════ (8-ب) إشعارات البريد الإلكتروني: القوالب وسجل الإرسال ═════════════
   جدولان جديدان (EmailTemplates, EmailNotificationLog) + القوالب الثلاثة الأولية + إعدادات التذكير الأسبوعي.
   هذا هو ترحيل EF Core نفسه (Migrations/EmailNotifications) كسكربت idempotent كما يولّده
   `dotnet ef migrations script --idempotent`، ونسخته المستقلة في database/migrations/.
   آمن لإعادة التشغيل: جدول __EFMigrationsHistory_EmailNotifications يمنع تكراره، ولا يمسّ أي جدول قائم.   */

IF OBJECT_ID(N'[__EFMigrationsHistory_EmailNotifications]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory_EmailNotifications] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory_EmailNotifications] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE TABLE [EmailNotificationLog] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWID()),
        [TemplateKey] nvarchar(50) NOT NULL,
        [ProjectId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NULL,
        [RecipientEmail] nvarchar(320) NULL,
        [Subject] nvarchar(300) NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'Pending',
        [SentAt] datetime2 NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [CorrelationKey] nvarchar(300) NOT NULL,
        [AttemptCount] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_EmailNotificationLog] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EmailNotificationLog_Status] CHECK ([Status] IN (N'Pending', N'Sent', N'Failed'))
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE TABLE [EmailTemplates] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWID()),
        [TemplateKey] nvarchar(50) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [Subject] nvarchar(300) NOT NULL,
        [BodyHtml] nvarchar(max) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedBy] uniqueidentifier NULL,
        CONSTRAINT [PK_EmailTemplates] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE INDEX [IX_EmailNotificationLog_Project_Template] ON [EmailNotificationLog] ([ProjectId], [TemplateKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE INDEX [IX_EmailNotificationLog_Status_UpdatedAt] ON [EmailNotificationLog] ([Status], [UpdatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_EmailNotificationLog_CorrelationKey] ON [EmailNotificationLog] ([CorrelationKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_EmailTemplates_TemplateKey] ON [EmailTemplates] ([TemplateKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] ON;
    EXEC(N'INSERT INTO [EmailTemplates] ([TemplateKey], [NameAr], [Subject], [BodyHtml], [IsEnabled])
    VALUES (N''ProjectAssigned'', N''إسناد مشروع'', N''تم إسناد مشروع جديد لك - {{ProjectName}}'', CONCAT(CAST(N''<div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">'' AS nvarchar(max)), nchar(10), N''<h2 style="color:#134586;margin:0 0 4px">إسناد مشروع جديد</h2>'', nchar(10), N''<p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع</p>'', nchar(10), N''<p>مرحبًا {{UserName}}،</p>'', nchar(10), N''<p>تم إسناد المشروع التالي إليك، وأنت الآن مسؤول عن متابعته وتحديث بياناته:</p>'', nchar(10), N''<table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">وصف مختصر</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectDescription}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الإدارة</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{DepartmentName}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">مدير المشروع / المسؤول</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectManagerName}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ البداية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStartDate}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ النهاية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectEndDate}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">حالة المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStatus}}</td></tr>'', nchar(10), N''</table>'', nchar(10), N''<p>يمكنك الدخول للمشروع من الرابط التالي:</p>'', nchar(10), N''<p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>'', nchar(10), N''<p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>'', nchar(10), N''<p>مع التحية.</p>'', nchar(10), N''<hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">'', nchar(10), N''<p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>'', nchar(10), N''</div>''), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] ON;
    EXEC(N'INSERT INTO [EmailTemplates] ([TemplateKey], [NameAr], [Subject], [BodyHtml], [IsEnabled])
    VALUES (N''ProjectWeeklyReminder'', N''التذكير الأسبوعي بالمشروع'', N''تذكير أسبوعي بمتابعة المشروع - {{ProjectName}}'', CONCAT(CAST(N''<div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">'' AS nvarchar(max)), nchar(10), N''<h2 style="color:#134586;margin:0 0 4px">تذكير أسبوعي بمتابعة المشروع</h2>'', nchar(10), N''<p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع</p>'', nchar(10), N''<p>مرحبًا {{UserName}}،</p>'', nchar(10), N''<p>هذا تذكير أسبوعي بالمشروع التالي الذي لم ينتهِ بعد. نأمل تحديث نسب الإنجاز وتسجيل التحديث الأسبوعي:</p>'', nchar(10), N''<table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الحالة الحالية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStatus}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ النهاية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectEndDate}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الأيام المتبقية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{DaysRemaining}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">عدد المهام المتأخرة</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{OverdueTasksCount}}</td></tr>'', nchar(10), N''</table>'', nchar(10), N''{{OverdueTasksList}}'', nchar(10), N''<p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>'', nchar(10), N''<p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>'', nchar(10), N''<p>مع التحية.</p>'', nchar(10), N''<hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">'', nchar(10), N''<p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>'', nchar(10), N''</div>''), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] ON;
    EXEC(N'INSERT INTO [EmailTemplates] ([TemplateKey], [NameAr], [Subject], [BodyHtml], [IsEnabled])
    VALUES (N''ProjectCompleted'', N''اكتمال المشروع'', N''تم إكمال المشروع - {{ProjectName}}'', CONCAT(CAST(N''<div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">'' AS nvarchar(max)), nchar(10), N''<h2 style="color:#134586;margin:0 0 4px">اكتمل المشروع</h2>'', nchar(10), N''<p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع</p>'', nchar(10), N''<p>مرحبًا {{UserName}}،</p>'', nchar(10), N''<p>نفيدكم بأن حالة المشروع التالي تحوّلت إلى «مكتمل»، شكرًا لجهودكم:</p>'', nchar(10), N''<table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ الإكمال</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectCompletedDate}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز النهائية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>'', nchar(10), N''<tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">مدة المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectDuration}}</td></tr>'', nchar(10), N''</table>'', nchar(10), N''<p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>'', nchar(10), N''<p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>'', nchar(10), N''<p>مع التحية.</p>'', nchar(10), N''<hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">'', nchar(10), N''<p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>'', nchar(10), N''</div>''), CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'TemplateKey', N'NameAr', N'Subject', N'BodyHtml', N'IsEnabled') AND [object_id] = OBJECT_ID(N'[EmailTemplates]'))
        SET IDENTITY_INSERT [EmailTemplates] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    IF OBJECT_ID(N'dbo.app_settings', N'U') IS NOT NULL
    BEGIN
      IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_enabled')
        INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_enabled', N'true');
      IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_day')
        INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_day', N'0');
      IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_time')
        INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_time', N'08:00');
    END
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260921224440_EmailNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_EmailNotifications] ([MigrationId], [ProductVersion])
    VALUES (N'20260921224440_EmailNotifications', N'8.0.11');
END;
GO

COMMIT;
GO

PRINT N'  ✓ (8-ب) جداول إشعارات البريد الإلكتروني جاهزة.';
GO


/* ═════════════ (8-ج) قوالب البريد: نص عادي بدل HTML ═════════════
   عمود MessageText الجديد على EmailTemplates: صفحة الإعدادات تحرّر منه فقرة نص عادية فقط، والإطار
   الثابت (الترويسة والجدول والزر) بات في كود الخادم (EmailTemplateShells) لا في القاعدة. BodyHtml أُبقي
   في الجدول (غير مستخدَم بعد الآن) تفاديًا لأي فقدان بيانات. مطابق لترحيل EF Core
   AddEmailTemplateMessageText، ونسخته المستقلة في database/migrations/.                                */

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260922051532_AddEmailTemplateMessageText'
)
BEGIN
    ALTER TABLE [EmailTemplates] ADD [MessageText] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260922051532_AddEmailTemplateMessageText'
)
BEGIN
    UPDATE [EmailTemplates] SET [MessageText] = N'تم إسناد المشروع التالي إليك، وأنت الآن مسؤول عن متابعته وتحديث بياناته:'
    WHERE [TemplateKey] = N'ProjectAssigned';

    UPDATE [EmailTemplates] SET [MessageText] = N'هذا تذكير أسبوعي بالمشروع التالي الذي لم ينتهِ بعد. نأمل تحديث نسب الإنجاز وتسجيل التحديث الأسبوعي:'
    WHERE [TemplateKey] = N'ProjectWeeklyReminder';

    UPDATE [EmailTemplates] SET [MessageText] = N'نفيدكم بأن حالة المشروع التالي تحوّلت إلى «مكتمل»، شكرًا لجهودكم:'
    WHERE [TemplateKey] = N'ProjectCompleted';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_EmailNotifications]
    WHERE [MigrationId] = N'20260922051532_AddEmailTemplateMessageText'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_EmailNotifications] ([MigrationId], [ProductVersion])
    VALUES (N'20260922051532_AddEmailTemplateMessageText', N'8.0.11');
END;
GO

COMMIT;
GO

PRINT N'  ✓ (8-ج) نص قوالب البريد أصبح حقلاً عاديًا (MessageText).';
GO


/* ═════════════ (9) حساب مدير النظام الأول وتجزئة كلمات المرور ═════════════ */

-- ⚠ بيانات الدخول الأولى — غيّر كلمة المرور من داخل النظام بعد أول دخول.
--   mobile_no متروك NULL عمدًا: لو وضعت رقم جوال فسيطلب النظام رمز تحقق برسالة
--   نصية عند كل دخول، ولن تستطيع الدخول قبل ضبط SMS_URL و SMS_WS_KEY.
DECLARE @adminEmail    NVARCHAR(150) = N'admin@example.com';
DECLARE @adminPassword NVARCHAR(255) = N'ChangeMe#Admin2026!';
DECLARE @adminName     NVARCHAR(100) = N'مدير النظام';
DECLARE @adminSalt     VARBINARY(16) = CRYPT_GEN_RANDOM(16);
DECLARE @adminPasswordHash NVARCHAR(255) = N'sha512$'
  + CONVERT(NVARCHAR(32), @adminSalt, 2) + N'$'
  + CONVERT(NVARCHAR(128), HASHBYTES(
      'SHA2_512', @adminSalt + CONVERT(VARBINARY(MAX), @adminPassword)
    ), 2);

IF NOT EXISTS (SELECT 1 FROM dbo.profiles WHERE email = @adminEmail AND ISNULL(status, 'active') <> 'deleted')
BEGIN
  INSERT INTO dbo.profiles (full_name, email, role, department, password_hash, status)
  VALUES (@adminName, @adminEmail, 'admin', N'الإدارة العامة', @adminPasswordHash, 'active');
  PRINT N'  ✓ (9) أُنشئ حساب مدير النظام بكلمة مرور مجزأة.';
END
ELSE
  PRINT N'  • (9) حساب مدير النظام موجود — لم يُنشأ من جديد.';
GO

-- ترقية كلمات المرور النصية القديمة فورًا داخل SQL. الصيغة المملّحة الانتقالية
-- يتعرّف عليها التطبيق، ثم يحولها إلى scrypt عند أول دخول ناجح.
DECLARE @passwordUserId UNIQUEIDENTIFIER;
DECLARE @plainPassword NVARCHAR(255);
DECLARE @passwordSalt VARBINARY(16);
DECLARE @passwordHash NVARCHAR(255);
DECLARE @passwordsMigrated INT = 0;

DECLARE plainPasswords CURSOR LOCAL FAST_FORWARD FOR
  SELECT id, password_hash
  FROM dbo.profiles
  WHERE password_hash IS NOT NULL
    AND password_hash NOT LIKE N'scrypt$%'
    AND password_hash NOT LIKE N'sha512$%';

OPEN plainPasswords;
FETCH NEXT FROM plainPasswords INTO @passwordUserId, @plainPassword;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @passwordSalt = CRYPT_GEN_RANDOM(16);
  SET @passwordHash = N'sha512$'
    + CONVERT(NVARCHAR(32), @passwordSalt, 2) + N'$'
    + CONVERT(NVARCHAR(128), HASHBYTES(
        'SHA2_512', @passwordSalt + CONVERT(VARBINARY(MAX), @plainPassword)
      ), 2);

  UPDATE dbo.profiles
  SET password_hash = @passwordHash
  WHERE id = @passwordUserId;

  SET @passwordsMigrated += 1;
  FETCH NEXT FROM plainPasswords INTO @passwordUserId, @plainPassword;
END
CLOSE plainPasswords;
DEALLOCATE plainPasswords;

PRINT N'  ✓ (9) كلمات المرور النصية التي جرى تحويلها: '
  + CAST(@passwordsMigrated AS NVARCHAR(20));
GO


/* ═════════════ (10) التحقق والتقرير النهائي ═════════════ */

DECLARE @expected TABLE (name SYSNAME);
INSERT INTO @expected (name) VALUES
  ('app_settings'), ('assignments'), ('audit_log'), ('login_otp'),
  ('meeting_actions'), ('milestones'), ('notification_log'), ('phase_notes'),
  ('profiles'), ('project_updates'), ('projects'), ('risks_issues'),
  ('tasks'), ('user_sessions'),
  ('EmailTemplates'), ('EmailNotificationLog');

DECLARE @missing INT = (SELECT COUNT(*) FROM @expected e WHERE OBJECT_ID('dbo.' + e.name, 'U') IS NULL);
DECLARE @idx     INT = (SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                        WHERE t.schema_id = SCHEMA_ID('dbo') AND i.name IS NOT NULL);
DECLARE @admins  INT = (SELECT COUNT(*) FROM dbo.profiles WHERE role = 'admin' AND ISNULL(status,'active') = 'active');
DECLARE @plainPasswords INT = (
  SELECT COUNT(*) FROM dbo.profiles
  WHERE password_hash IS NOT NULL
    AND password_hash NOT LIKE N'scrypt$%'
    AND password_hash NOT LIKE N'sha512$%'
);

PRINT N'';
PRINT N'═══════════════════════════════════════════════════════════';

IF @missing > 0 OR @plainPasswords > 0
BEGIN
  PRINT N'  ✗ الترقية لم تجتز التحقق النهائي.';
  IF @missing > 0
  BEGIN
    PRINT N'    الجداول المفقودة:';
    SELECT N'✗ ' + e.name AS [جدول مفقود] FROM @expected e WHERE OBJECT_ID('dbo.' + e.name, 'U') IS NULL;
  END
  IF @plainPasswords > 0
    PRINT N'    كلمات مرور غير مجزأة متبقية: ' + CAST(@plainPasswords AS NVARCHAR(20));
  PRINT N'    راجع الأخطاء أعلاه.';
END
ELSE
BEGIN
  PRINT N'  ✓ التثبيت/الترقية اكتملت بنجاح';
  PRINT N'';
  PRINT N'  القاعدة          : ' + DB_NAME();
  PRINT N'  الجداول          : 14 / 14';
  PRINT N'  الفهارس والمفاتيح: ' + CAST(@idx AS NVARCHAR(10));
  PRINT N'  مديرو نظام نشطون: ' + CAST(@admins AS NVARCHAR(10));
  PRINT N'  كلمات مرور نصية  : 0';
  PRINT N'';
  PRINT N'  ─── الدخول للنظام ───';
  PRINT N'    البريد       : admin@example.com';
  PRINT N'    كلمة المرور  : ChangeMe#Admin2026!';
  PRINT N'    ⚠ غيّرها من داخل النظام بعد أول دخول.';
  PRINT N'';
  PRINT N'  ─── انسخ هذا إلى .env.local ───';
  PRINT N'    DB_SERVER=' + @@SERVERNAME;
  PRINT N'    DB_NAME=' + DB_NAME();

  IF (SELECT loginFailed FROM #state) = 1
  BEGIN
    PRINT N'    DB_USER=<لم يُنشأ tracker_user — راجع تنبيه القسم (2) أعلاه>';
    PRINT N'    DB_PASSWORD=<...>';
  END
  ELSE IF (SELECT loginCreated FROM #state) = 1
  BEGIN
    PRINT N'    DB_USER=tracker_user';
    PRINT N'    DB_PASSWORD="ChangeMe#Sql2026!"';
  END
  ELSE
  BEGIN
    -- الحساب كان موجودًا قبل هذا التشغيل، فكلمة المرور الافتراضية ليست كلمته.
    PRINT N'    DB_USER=tracker_user';
    PRINT N'    DB_PASSWORD=<كلمة المرور الحالية للحساب — الحساب كان موجودًا مسبقًا>';
    PRINT N'      لتعيين كلمة جديدة من SSMS:';
    PRINT N'      ALTER LOGIN [tracker_user] WITH PASSWORD = N''كلمة-جديدة'';';
  END

  PRINT N'    SESSION_SECRET=<ولّده بالأمر أدناه — 32 حرفًا فأكثر وإلا رُفض كل دخول>';
  PRINT N'';
  PRINT N'    node -e "console.log(require(''crypto'').randomBytes(48).toString(''base64url''))"';
END

PRINT N'═══════════════════════════════════════════════════════════';
GO

-- تنبيه أخير: جداول تطبيق متروكة داخل master من محاولة تثبيت سابقة فاشلة.
-- لا يحذفها هذا الملف — الحذف من قاعدة نظام قرارك أنت.
IF EXISTS (
  SELECT 1 FROM master.sys.tables t
  JOIN master.sys.schemas s ON s.schema_id = t.schema_id
  WHERE s.name = 'dbo' AND t.name IN
    ('profiles','projects','milestones','tasks','phase_notes','assignments',
     'project_updates','risks_issues','meeting_actions','audit_log','app_settings',
     'user_sessions','login_otp','notification_log'))
BEGIN
  PRINT N'';
  PRINT N'  ⚠ تنبيه: توجد جداول تطبيق داخل قاعدة النظام [master] من محاولة سابقة فاشلة.';
  PRINT N'    لا تؤثر على التثبيت أعلاه. خذ نسخة احتياطية ثم اطلب من مسؤول قاعدة';
  PRINT N'    البيانات حذف جداول التطبيق من master بعد التأكد أنها ليست مستخدمة.';
END
GO

SET NOEXEC OFF;
GO

IF OBJECT_ID(N'tempdb..#state') IS NOT NULL DROP TABLE #state;
GO
-- انتهى: تثبيت وترقية وتجزئة كلمات المرور في ملف واحد، آمن لإعادة التشغيل.
