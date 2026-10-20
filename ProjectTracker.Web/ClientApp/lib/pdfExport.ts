
/**
 * html2canvas 1.4.1 يقيس خط أساس النص في المستند الأصلي بصورة 1×1 (SMALL_IMAGE) محاذاةً على
 * baseline، لكن Tailwind preflight يجعل كل img بـ display:block فتنزل الصورة إلى سطر مستقل ويُقاس ارتفاع السطر كاملًا
 * بدل خط الأساس. النتيجة كانت نصًا يُرسم في PDF أدنى من موضعه بنحو نصف سطر (7px لخط 14px و10px لخط 20px) فيُقص داخل
 * الخلايا ويبدو غير واضح. قيس بالتشغيل مقابل رسم المتصفح: الانحراف بعد هذه القاعدة ≤ 1px لكل الأحجام والخطوط.
 * القاعدة مؤقتة (تُزال بعد الالتقاط) ومحصورة في صورة القياس نفسها.
 */
const HTML2CANVAS_PROBE_IMAGE = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'

async function withHtml2canvasBaselineFix<T>(capture: () => Promise<T>): Promise<T> {
  const style = document.createElement('style')
  style.textContent = `img[src="${HTML2CANVAS_PROBE_IMAGE}"] { display: inline !important; }`
  document.head.appendChild(style)
  try {
    return await capture()
  } finally {
    style.remove()
  }
}

/** تحويل كل لون حديث في النسخة المستنسخة إلى rgba() مع الحفاظ على الشفافية. */
function normalizePdfCloneColors(clonedDocument: Document) {
  const modernColor = /(oklab|oklch|lab|lch|color-mix|color)\(/i
  // دالة لون حديثة واحدة مع قوس متداخل مستوى واحد (color-mix(in oklab, oklch(...) 20%, ...)).
  const modernColorToken = /(?:oklab|oklch|lab|lch|color-mix|color)\((?:[^()]|\([^()]*\))*\)/gi
  const colorProps = [
    'color',
    'background-color',
    'border-top-color',
    'border-right-color',
    'border-bottom-color',
    'border-left-color',
    'outline-color',
    'text-decoration-color',
    'caret-color',
    'column-rule-color',
    'fill',
    'stroke',
    'stop-color',
  ]
  const compositeProps = ['box-shadow', 'text-shadow', 'background-image']

  const canvas = document.createElement('canvas')
  canvas.width = 1
  canvas.height = 1
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  const cache = new Map<string, string>()

  function toRgba(value: string): string {
    const cached = cache.get(value)
    if (cached) return cached
    let result = 'rgba(0, 0, 0, 0)'
    if (ctx && typeof CSS !== 'undefined' && CSS.supports('color', value)) {
      ctx.clearRect(0, 0, 1, 1)
      ctx.fillStyle = value
      ctx.fillRect(0, 0, 1, 1)
      const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data
      result = `rgba(${r}, ${g}, ${b}, ${Math.round((a / 255) * 1000) / 1000})`
    }
    cache.set(value, result)
    return result
  }

  clonedDocument.querySelectorAll<HTMLElement>('*').forEach((element) => {
    const styles = clonedDocument.defaultView?.getComputedStyle(element)
    if (!styles) return

    colorProps.forEach((property) => {
      const value = styles.getPropertyValue(property).trim()
      if (value && modernColor.test(value)) {
        element.style.setProperty(property, toRgba(value), 'important')
      }
    })

    compositeProps.forEach((property) => {
      const value = styles.getPropertyValue(property)
      if (value && modernColor.test(value)) {
        element.style.setProperty(property, value.replace(modernColorToken, token => toRgba(token)), 'important')
      }
    })
  })
}

async function waitForReportAssets(root: HTMLElement) {
  if ('fonts' in document) await document.fonts.ready
  const pendingImages = Array.from(root.querySelectorAll('img')).filter(image => !image.complete)
  await Promise.all(pendingImages.map(image => new Promise<void>(resolve => {
    image.addEventListener('load', () => resolve(), { once: true })
    image.addEventListener('error', () => resolve(), { once: true })
  })))
}

/**
 * يصدر صفحات تقرير مُعدّة مسبقًا بدل قص صورة طويلة عشوائيًا. كل عنصر
 * data-pdf-page يتحول إلى صفحة A4 مستقلة، لذلك تبقى الصفوف والرؤوس والتذييلات
 * ضمن الصفحة التي رتّبها مكوّن التقرير.
 */
export async function exportReportPagesToPdf(root: HTMLElement, fileName: string) {
  await waitForReportAssets(root)
  const pages = Array.from(root.querySelectorAll<HTMLElement>('[data-pdf-page="true"]'))
  if (pages.length === 0) throw new Error('No report pages were found')

  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import('html2canvas'), import('jspdf')])
  const pdf = new jsPDF('landscape', 'mm', 'a4')
  const pageWidth = pdf.internal.pageSize.getWidth()
  const pageHeight = pdf.internal.pageSize.getHeight()

  for (let index = 0; index < pages.length; index += 1) {
    const page = pages[index]
    const canvas = await withHtml2canvasBaselineFix(() => html2canvas(page, {
      scale: 2,
      backgroundColor: '#FFFFFF',
      useCORS: true,
      scrollX: 0,
      scrollY: 0,
      windowWidth: Math.max(1120, page.scrollWidth),
      onclone: normalizePdfCloneColors,
    }))
    if (index > 0) pdf.addPage('a4', 'landscape')
    pdf.addImage(canvas.toDataURL('image/jpeg', 0.9), 'JPEG', 0, 0, pageWidth, pageHeight)
  }

  pdf.save(`${fileName.replace(/[\\/:*?"<>|]/g, '-')}.pdf`)
}

