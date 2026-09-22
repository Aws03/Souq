// ============================================================================
// منطقُ شاشات الفوترة، نقيٌّ ومُختبَر وحده (C5، ADR-0056).
//
// **ولماذا يعيش خارج الشاشة؟** لأنّ ما هنا كلُّه أسئلةٌ لها جوابٌ واحد صحيح — أيُّ حالةٍ تُعرض،
// أيُّ فعلٍ متاح، ما الذي يمنع الإصدار — وهي بالضبط ما لا يجوز أن يُجاب مرّتين بشكلين في شاشتين.
//
// **ولا شيء هنا يقرّر**: الخادم وحده يرفض ويقبل (FrontendGuide). ما هنا يقرّر ما **يُعرض**، وإخفاءُ
// زرٍّ لا يمنع شيئاً — يمنع أن يُضغط زرٌّ جوابُه معروفٌ مسبقاً بالرفض.
// ============================================================================

/**
 * الحالةُ كما تُعرَض. **«متأخّرة» ليست حالةً يرسلها الخادم** — هي `Issued` ومرّ استحقاقُها،
 * والخادم يحسبها ويرسلها علَماً. ودمجُها في مفردةٍ واحدة هنا يجعل الشارة تقول ما يهمّ التاجر
 * فعلاً بدل أن تقول «صادرة» لفاتورةٍ تأخّرت شهراً.
 * @param {{ status: string, isOverdue?: boolean }} invoice
 */
export const displayStatus = (invoice) =>
  (invoice?.isOverdue && invoice.status === 'Issued' ? 'Overdue' : invoice?.status ?? 'Draft');

/** مسوّدةٌ تُحرَّر: أسطرُها تُضاف وتُحذف، وتُلغى، وتُصدَر. */
export const isDraft = (invoice) => invoice?.status === 'Draft';

/**
 * مستندٌ صدر: يُسجَّل عليه سدادٌ ويُقابَل بإشعار دائن — **ولا يُحرَّر ولا يُلغى أبداً**.
 * و`Settled` منه: إشعارُ دائنٍ على فاتورةٍ سُدّدت بالكامل لا معنى له، لكنّ قراءتَها تبقى.
 */
export const isIssued = (invoice) => invoice?.status === 'Issued' || invoice?.status === 'Settled';

/** يبقى عليها شيء ⇒ تقبل سداداً أو قيداً دائناً. */
export const hasOutstanding = (invoice) => isIssued(invoice) && Number(invoice?.outstanding ?? 0) > 0;

/**
 * سببُ تعذُّر الإصدار كما تقرؤه الشاشة — من الإعداد أوّلاً، ثمّ من الفاتورة نفسها.
 *
 * والترتيب مقصود: «اضبط العملة» يسبق «أضف سطراً»، لأنّ الأوّل يمنع كلَّ فاتورة والثاني يمنع هذه.
 * @param {{ canIssue?: boolean, blockingReason?: string | null } | null | undefined} settings
 * @param {{ status?: string, lines?: unknown[] } | null | undefined} invoice
 * @returns {string | null} مفتاحُ سببٍ ثابت، أو null حين لا مانع
 */
export function issueBlocker(settings, invoice) {
  if (!settings?.canIssue) return settings?.blockingReason ?? 'BillingSettingsMissing';
  if (!isDraft(invoice)) return 'AlreadyIssued';
  if (!invoice?.lines?.length) return 'NoLines';
  return null;
}

/**
 * مجموعُ سطرٍ كما تعرضه الشاشة **قبل** أن يُرسَل. تقريبٌ إلى ثلاث خانات: الخادم يقرّب بخانات
 * العملة الصغرى، وأكثرُها في ISO 4217 ثلاث. وهذا عرضٌ لا حساب — الرقمُ المُلزِم يأتي من الخادم
 * بعد الحفظ، وهذه معاينةٌ تمنع مفاجأةً لا تُنتج مبلغاً.
 * @param {number | string} quantity
 * @param {number | string} unitAmount
 */
export function previewLineTotal(quantity, unitAmount) {
  // حقلٌ فارغ ليس صفراً: `Number('')` تساوي صفراً، فبلا هذا الفحص تعرض الشاشة «مجموع السطر: 0»
  // لحقلٍ لم يُملأ بعد — رقمٌ يبدو محسوباً وهو ليس كذلك. أمسكه اختبارُه قبل أن يراه مشغّل.
  const blank = (value) => value === '' || value === null || value === undefined;
  if (blank(quantity) || blank(unitAmount)) return null;

  const q = Number(quantity);
  const u = Number(unitAmount);
  if (!Number.isFinite(q) || !Number.isFinite(u)) return null;
  return Math.round(q * u * 1000) / 1000;
}

/**
 * مشاكلُ سطرٍ قبل الإرسال. الخادم يفحصها كلَّها أيضاً — هذه تُجنّب رحلةً وتقول أين الخطأ.
 * @param {{ description?: string, quantity?: unknown, unitAmount?: unknown }} line
 */
export function lineProblems(line) {
  const problems = {};
  if (!String(line?.description ?? '').trim()) problems.description = 'required';
  const q = Number(line?.quantity);
  if (!Number.isFinite(q) || q <= 0) problems.quantity = 'positive';
  const u = Number(line?.unitAmount);
  if (!Number.isFinite(u) || u < 0) problems.unitAmount = 'nonNegative';
  return problems;
}

/**
 * مشاكلُ سدادٍ قبل الإرسال. **الحدُّ الأعلى هو المتبقّي**: الخادم يرفض ما يتجاوزه، وقولُ ذلك هنا
 * يمنع مشغّلاً من إدخال رقمٍ ثمّ قراءة رفضٍ لا يفهم سببه.
 * @param {{ amount?: unknown, receivedAtUtc?: string }} payment
 * @param {number} outstanding
 */
export function paymentProblems(payment, outstanding) {
  const problems = {};
  const amount = Number(payment?.amount);
  if (!Number.isFinite(amount) || amount <= 0) problems.amount = 'positive';
  else if (amount > Number(outstanding)) problems.amount = 'exceedsOutstanding';
  if (!payment?.receivedAtUtc) problems.receivedAtUtc = 'required';
  return problems;
}

/** طرقُ التحصيل التي يقبلها الخادم — بلا مزوّد، وكلُّها أفعالُ بشر خارج النظام. */
export const PAYMENT_METHODS = ['BankTransfer', 'Cash', 'Cheque', 'Other'];

/** حالاتُ الفاتورة كما يرشّح بها المشغّل. `Overdue` غيرُ مذكورة: لها مرشّحُها الخاصّ. */
export const INVOICE_STATUSES = ['Draft', 'Issued', 'Settled', 'Cancelled'];

/**
 * مرشّحاتُ القائمة من سلسلة الاستعلام — والعكس. الحالةُ في العنوان لا في الذاكرة، فزرُّ الرجوع
 * يُعيد ما كان يراه المشغّل ورابطٌ يُنسَخ يصل إلى الشيء نفسه (نمط `Audit.jsx`).
 * @param {URLSearchParams} search
 */
export const filtersFromSearch = (search) => ({
  status: search.get('status') ?? '',
  tenantId: search.get('tenantId') ?? '',
  overdueOnly: search.get('overdueOnly') === 'true',
  q: search.get('q') ?? '',
});

/** @param {{ status?: string, tenantId?: string, overdueOnly?: boolean, q?: string }} filters */
export function searchFromFilters(filters, page = 1) {
  const params = new URLSearchParams();
  if (filters.status) params.set('status', filters.status);
  if (filters.tenantId) params.set('tenantId', String(filters.tenantId));
  if (filters.overdueOnly) params.set('overdueOnly', 'true');
  if (filters.q) params.set('q', filters.q);
  if (page > 1) params.set('page', String(page));
  return params;
}

/** @param {URLSearchParams} search */
export const pageFromSearch = (search) => {
  const page = Number(search.get('page'));
  return Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1;
};

/**
 * ما يُرسَل إلى الخادم للقائمة. القيمُ الفارغة تُحذف لا تُرسَل فارغةً: `toQueryString` يُسقطها،
 * لكنّ حذفَها هنا يجعل مفتاحَ الذاكرة المؤقّتة واحداً لحالتين متطابقتين.
 */
export function invoiceQuery(filters, page, pageSize) {
  return {
    status: filters.status || undefined,
    tenantId: filters.tenantId || undefined,
    overdueOnly: filters.overdueOnly || undefined,
    search: filters.q || undefined,
    page,
    pageSize,
  };
}
