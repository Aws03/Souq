import { useId, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import Button from '../../components/common/Button';
import Skeleton from '../../components/common/Skeleton';
import StatusBadge from '../../components/common/StatusBadge';
import { ErrorBanner } from '../../components/common/StateViews';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { formatPrice } from '../../components/product/ProductBadges';
import { useToast } from '../../context/ToastContext';
import { statusTone } from '../../features/statusTone';
import { formatDate, formatDateTime } from '../../i18n';
import {
  PAYMENT_METHODS, displayStatus, hasOutstanding, isDraft, isIssued, issueBlocker,
  lineProblems, paymentProblems, previewLineTotal,
} from '../../features/platform/invoices';
import styles from './Platform.module.css';

// ============================================================================
// فاتورةُ اشتراكٍ واحدة من المنصّة (C5، ADR-0056): تُحرَّر مسوّدةً، وتُصدَر مرّةً، ثمّ تُحصَّل.
//
// **والشاشةُ مبنيّةٌ على الحدّ الذي يفرضه المجال، لا على أدبٍ في العرض.** المسوّدةُ تُظهر تحرير
// الأسطر والإصدار والإلغاء؛ والمستندُ الصادر يُظهر السدادَ وإشعارَ الدائن ولا يُظهر بابَ تحريرٍ
// واحداً — لأنّه لا يوجد: الخادم يردّ 422 على كلٍّ منها، وإخفاؤها يمنع ضغطةً جوابُها معروف.
//
// **والإصدار لا رجعةَ فيه، فيُؤكَّد بحوار.** بعده: رقمٌ من سلسلة سوق، ولقطةُ ضريبةٍ مجمَّدة،
// ومستندٌ يراه التاجر في لوحته. وذلك أقربُ ما في هذه اللوحة إلى فعلٍ لا يُتراجَع عنه.
// ============================================================================
export default function InvoiceDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const toast = useToast();
  const queryClient = useQueryClient();
  const confirmation = useConfirmAction();

  const { data: invoice, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.platformInvoice(id),
    queryFn: () => api.getPlatformInvoice(id),
  });

  const settings = useQuery({
    queryKey: queryKeys.platformBillingSettings(),
    queryFn: api.getPlatformBillingSettings,
  });

  // إبطالُ الجذر لا المفتاح: الفعلُ الواحد يغيّر المستند **والقائمة** معاً.
  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.platformInvoicesAll() });

  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;
  if (isPending || !invoice) return <Skeleton height={420} radius={14} />;

  const blocker = issueBlocker(settings.data, invoice);
  const money = (amount) => formatPrice(amount, invoice.currency);

  const issue = () => confirmation.ask({
    title: t('platform.invoices.confirmIssue.title'),
    message: t('platform.invoices.confirmIssue.message', { total: money(invoice.total) }),
    confirmLabel: t('platform.invoices.confirmIssue.action'),
    action: async () => {
      const { number } = await api.issuePlatformInvoice(invoice.id, {});
      await reload();
      toast.success(t('platform.invoices.issued', { number }));
    },
  });

  const cancel = () => confirmation.ask({
    title: t('platform.invoices.confirmCancel.title'),
    message: t('platform.invoices.confirmCancel.message'),
    confirmLabel: t('platform.invoices.confirmCancel.action'),
    danger: true,
    action: async () => {
      await api.cancelPlatformInvoice(invoice.id);
      await reload();
      toast.success(t('platform.invoices.cancelled'));
    },
  });

  return (
    <div>
      <Link to="/platform/invoices" className={styles.back}>{t('platform.invoices.backToList')}</Link>

      <div className={styles.pageHead}>
        <div>
          <h1 className={styles.title}>
            {invoice.number
              ? <span dir="ltr">{invoice.number}</span>
              : t('platform.invoices.unnumbered')}
          </h1>
          <p className={styles.subtitle}>
            {t('platform.invoices.forStore', { name: invoice.tenantName ?? t('platform.invoices.unknownStore') })}
          </p>
        </div>
        <StatusBadge tone={statusTone('invoice', displayStatus(invoice))}>
          {t(`platform.invoices.status.${displayStatus(invoice)}`)}
        </StatusBadge>
      </div>

      <section className={styles.panel} aria-labelledby="invoice-totals">
        <h2 id="invoice-totals" className={styles.panelTitle}>{t('platform.invoices.totals')}</h2>
        <dl className={styles.figures}>
          <div className={styles.figure}>
            <dt className={styles.figureLabel}>{t('platform.invoices.subtotal')}</dt>
            <dd className={styles.figureValue}>{money(invoice.subtotal)}</dd>
          </div>
          <div className={styles.figure}>
            <dt className={styles.figureLabel}>{t('platform.invoices.tax')}</dt>
            <dd className={styles.figureValue}>{money(invoice.taxAmount)}</dd>
          </div>
          <div className={styles.figure}>
            <dt className={styles.figureLabel}>{t('platform.invoices.total')}</dt>
            <dd className={styles.figureValue}>{money(invoice.total)}</dd>
          </div>
          <div className={styles.figure}>
            <dt className={styles.figureLabel}>{t('platform.invoices.paid')}</dt>
            <dd className={styles.figureValue}>{money(invoice.amountPaid)}</dd>
          </div>
          {invoice.credited > 0 && (
            <div className={styles.figure}>
              <dt className={styles.figureLabel}>{t('platform.invoices.credited')}</dt>
              <dd className={styles.figureValue}>{money(invoice.credited)}</dd>
            </div>
          )}
          <div className={styles.figure}>
            <dt className={styles.figureLabel}>{t('platform.invoices.outstanding')}</dt>
            <dd className={styles.figureValue}>{money(invoice.outstanding)}</dd>
          </div>
        </dl>

        <dl className={styles.facts}>
          <dt>{t('platform.invoices.period')}</dt>
          <dd>{formatDate(invoice.periodStartUtc)} — {formatDate(invoice.periodEndUtc)}</dd>
          {invoice.issuedAtUtc && (<>
            <dt>{t('platform.invoices.issuedAt')}</dt>
            <dd>{formatDateTime(invoice.issuedAtUtc)}</dd>
          </>)}
          {invoice.dueAtUtc && (<>
            <dt>{t('platform.invoices.dueAt')}</dt>
            <dd>
              {formatDate(invoice.dueAtUtc)}
              {invoice.isOverdue && (
                <> <span className={styles.attention}>
                  {t('platform.invoices.overdueBy', { count: invoice.daysOverdue })}
                </span></>
              )}
            </dd>
          </>)}
          {invoice.issuerName && (<>
            <dt>{t('platform.invoices.issuer')}</dt>
            <dd>{invoice.issuerName}</dd>
          </>)}
          {invoice.billedToName && (<>
            <dt>{t('platform.invoices.billedTo')}</dt>
            <dd>{invoice.billedToName}</dd>
          </>)}
        </dl>

        {/* ما جُمّد عند الإصدار يُقال صراحةً: هذه ليست قيمَ اليوم، بل قيمُ يوم صدرت. */}
        {isIssued(invoice) && (
          <p className={styles.panelHint}>{t('platform.invoices.frozenHint')}</p>
        )}
      </section>

      <LinesPanel invoice={invoice} money={money} onChanged={reload} />

      {invoice.taxSnapshot && <TaxSnapshotPanel snapshot={invoice.taxSnapshot} money={money} />}

      {isIssued(invoice) && <PaymentsPanel invoice={invoice} money={money} onChanged={reload} />}

      {isIssued(invoice) && <CreditNotesPanel invoice={invoice} money={money} onChanged={reload} />}

      {isDraft(invoice) && (
        <section className={styles.panel} aria-labelledby="invoice-actions">
          <h2 id="invoice-actions" className={styles.panelTitle}>{t('platform.invoices.draftActions')}</h2>
          {blocker && <p className={styles.panelHint}>{t(`platform.invoices.cannotIssue.${blocker}`)}</p>}
          <div className={styles.formActions}>
            <Button variant="secondary" onClick={cancel}>{t('platform.invoices.cancelDraft')}</Button>
            <Button variant="primary" disabled={!!blocker} onClick={issue}>{t('platform.invoices.issue')}</Button>
          </div>
        </section>
      )}

      {confirmation.dialog}
    </div>
  );
}

// ── الأسطر: تُحرَّر في المسوّدة، وتُقرأ بعد الإصدار ──────────────────────────
function LinesPanel({ invoice, money, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const editable = isDraft(invoice);
  const ids = { description: useId(), quantity: useId(), unitAmount: useId() };
  const [line, setLine] = useState({ description: '', quantity: '1', unitAmount: '' });
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const problems = submitted ? lineProblems(line) : {};
  const preview = previewLineTotal(line.quantity, line.unitAmount);

  const add = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.keys(lineProblems(line)).length > 0) return;
    setBusy(true); setError(null);
    try {
      await api.addPlatformInvoiceLine(invoice.id, {
        description: line.description.trim(),
        quantity: Number(line.quantity),
        unitAmount: Number(line.unitAmount),
        taxCategory: null,
      });
      setLine({ description: '', quantity: '1', unitAmount: '' });
      setSubmitted(false);
      await onChanged();
      toast.success(t('platform.invoices.lineAdded'));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const remove = async (lineId) => {
    try {
      await api.removePlatformInvoiceLine(invoice.id, lineId);
      await onChanged();
    } catch (err) {
      toast.error(err.message);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="invoice-lines">
      <h2 id="invoice-lines" className={styles.panelTitle}>{t('platform.invoices.lines')}</h2>

      {invoice.lines.length === 0
        ? <p className={styles.panelHint}>{t('platform.invoices.noLines')}</p>
        : (
          <ul className={styles.rows}>
            {invoice.lines.map((row) => (
              <li key={row.id} className={styles.row}>
                <span className={styles.rowMain}>{row.description}</span>
                <span className={styles.rowTags}>
                  {t('platform.invoices.lineQuantity', { quantity: row.quantity })}
                  {' · '}
                  {money(row.unitAmount)}
                  {' · '}
                  <strong>{money(row.lineTotal)}</strong>
                </span>
                {editable && (
                  <Button variant="ghost" size="sm" onClick={() => remove(row.id)}>
                    {t('common.delete')}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}

      {editable && (
        <form onSubmit={add} noValidate>
          {error && <ErrorBanner message={error} />}
          <div className={styles.formGrid}>
            <div className={styles.field}>
              <label htmlFor={ids.description} className={styles.label}>{t('platform.invoices.lineDescription')}</label>
              <input
                id={ids.description}
                className={styles.input}
                value={line.description}
                maxLength={300}
                aria-invalid={!!problems.description}
                onChange={(e) => setLine((l) => ({ ...l, description: e.target.value }))}
              />
              {problems.description && (
                <span className={styles.fieldError}>{t(`platform.invoices.lineProblem.${problems.description}`)}</span>
              )}
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.quantity} className={styles.label}>{t('platform.invoices.lineQuantityLabel')}</label>
              <input
                id={ids.quantity}
                className={`${styles.input} ${styles.mono}`}
                dir="ltr"
                type="number"
                step="0.0001"
                min="0"
                value={line.quantity}
                aria-invalid={!!problems.quantity}
                onChange={(e) => setLine((l) => ({ ...l, quantity: e.target.value }))}
              />
              {problems.quantity && (
                <span className={styles.fieldError}>{t(`platform.invoices.lineProblem.${problems.quantity}`)}</span>
              )}
            </div>

            <div className={styles.field}>
              {/* العملة لا تُدخَل ولا تُعرَض هنا كحقل: هي عملةُ الفاتورة، ويكتبها الخادم. */}
              <label htmlFor={ids.unitAmount} className={styles.label}>{t('platform.invoices.lineUnitAmount')}</label>
              <input
                id={ids.unitAmount}
                className={`${styles.input} ${styles.mono}`}
                dir="ltr"
                type="number"
                step="0.001"
                min="0"
                value={line.unitAmount}
                aria-invalid={!!problems.unitAmount}
                onChange={(e) => setLine((l) => ({ ...l, unitAmount: e.target.value }))}
              />
              <span className={problems.unitAmount ? styles.fieldError : styles.hint}>
                {problems.unitAmount
                  ? t(`platform.invoices.lineProblem.${problems.unitAmount}`)
                  : (preview !== null ? t('platform.invoices.linePreview', { total: money(preview) }) : ' ')}
              </span>
            </div>
          </div>

          <div className={styles.formActions}>
            <Button type="submit" variant="secondary" loading={busy}>{t('platform.invoices.addLine')}</Button>
          </div>
        </form>
      )}
    </section>
  );
}

// ── لقطةُ الضريبة: قيمٌ مجمَّدة، لا مرجعٌ يتحرّك ─────────────────────────────
function TaxSnapshotPanel({ snapshot, money }) {
  const { t } = useTranslation();
  return (
    <section className={styles.panel} aria-labelledby="invoice-tax">
      <h2 id="invoice-tax" className={styles.panelTitle}>{t('platform.invoices.taxSnapshot')}</h2>
      <dl className={styles.facts}>
        <dt>{t('platform.invoices.jurisdiction')}</dt>
        <dd><span dir="ltr">{snapshot.jurisdiction}</span></dd>
        <dt>{t('platform.invoices.priceMode')}</dt>
        <dd>{t(`platform.invoices.priceMode.${snapshot.priceMode}`)}</dd>
        {/* حالةُ التحقّق تظهر حيث يظهر الرقم — قاعدةُ ADR-0055 نفسها، وهي الفرق بين
            «قيمةٌ في القاعدة» و«قيمةٌ يُعتمد عليها». */}
        <dt>{t('platform.invoices.verification')}</dt>
        <dd>{t(`platform.tax.verification.${snapshot.verification}`)}</dd>
      </dl>
      <ul className={styles.rows}>
        {snapshot.lines.map((rate) => (
          <li key={rate.code} className={styles.row}>
            <span className={styles.rowMain}>{rate.name}</span>
            <span className={styles.rowTags}>
              <span dir="ltr">{rate.basisPoints / 100}%</span>
              {' · '}
              <strong>{money(rate.amount)}</strong>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

// ── التحصيل: فعلُ إنسانٍ وقع خارج النظام، يُسجَّل بعد وقوعه ─────────────────
function PaymentsPanel({ invoice, money, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const ids = { amount: useId(), method: useId(), date: useId(), reference: useId() };
  const today = new Date().toISOString().slice(0, 10);
  const [form, setForm] = useState({
    amount: '', method: PAYMENT_METHODS[0], receivedAtUtc: today, reference: '', note: '',
  });
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const problems = submitted ? paymentProblems(form, invoice.outstanding) : {};

  const record = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    if (Object.keys(paymentProblems(form, invoice.outstanding)).length > 0) return;
    setBusy(true); setError(null);
    try {
      await api.recordPlatformInvoicePayment(invoice.id, {
        amount: Number(form.amount),
        method: form.method,
        // تاريخٌ بلا وقت: المشغّل يسجّل يومَ وصول الحوالة كما في كشف بنكه.
        receivedAtUtc: new Date(`${form.receivedAtUtc}T00:00:00Z`).toISOString(),
        reference: form.reference.trim() || null,
        note: form.note.trim() || null,
      });
      setForm((f) => ({ ...f, amount: '', reference: '', note: '' }));
      setSubmitted(false);
      await onChanged();
      toast.success(t('platform.invoices.paymentRecorded'));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="invoice-payments">
      <h2 id="invoice-payments" className={styles.panelTitle}>{t('platform.invoices.payments')}</h2>
      <p className={styles.panelHint}>{t('platform.invoices.paymentsHint')}</p>

      {invoice.payments.length === 0
        ? <p className={styles.panelHint}>{t('platform.invoices.noPayments')}</p>
        : (
          <ul className={styles.rows}>
            {invoice.payments.map((payment) => (
              <li key={payment.id} className={styles.row}>
                <span className={styles.rowMain}>{money(payment.amount)}</span>
                <span className={styles.rowTags}>
                  {t(`platform.invoices.method.${payment.method}`)}
                  {' · '}
                  {formatDate(payment.receivedAtUtc)}
                  {payment.reference && <> · <span dir="ltr">{payment.reference}</span></>}
                  {/* مَن سجّل الإقرار بوصول المال، على المستند نفسه لا في سجلّ التدقيق وحده. */}
                  {payment.recordedByEmail && <> · <span dir="ltr">{payment.recordedByEmail}</span></>}
                </span>
              </li>
            ))}
          </ul>
        )}

      {hasOutstanding(invoice) && (
        <form onSubmit={record} noValidate>
          {error && <ErrorBanner message={error} />}
          <div className={styles.formGrid}>
            <div className={styles.field}>
              <label htmlFor={ids.amount} className={styles.label}>{t('platform.invoices.paymentAmount')}</label>
              <input
                id={ids.amount}
                className={`${styles.input} ${styles.mono}`}
                dir="ltr"
                type="number"
                step="0.001"
                min="0"
                value={form.amount}
                aria-invalid={!!problems.amount}
                onChange={(e) => setForm((f) => ({ ...f, amount: e.target.value }))}
              />
              <span className={problems.amount ? styles.fieldError : styles.hint}>
                {problems.amount
                  ? t(`platform.invoices.paymentProblem.${problems.amount}`, { outstanding: money(invoice.outstanding) })
                  : t('platform.invoices.outstandingHint', { outstanding: money(invoice.outstanding) })}
              </span>
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.method} className={styles.label}>{t('platform.invoices.paymentMethod')}</label>
              <select
                id={ids.method}
                className={styles.input}
                value={form.method}
                onChange={(e) => setForm((f) => ({ ...f, method: e.target.value }))}
              >
                {PAYMENT_METHODS.map((m) => (
                  <option key={m} value={m}>{t(`platform.invoices.method.${m}`)}</option>
                ))}
              </select>
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.date} className={styles.label}>{t('platform.invoices.paymentDate')}</label>
              <input
                id={ids.date}
                className={styles.input}
                type="date"
                value={form.receivedAtUtc}
                aria-invalid={!!problems.receivedAtUtc}
                onChange={(e) => setForm((f) => ({ ...f, receivedAtUtc: e.target.value }))}
              />
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.reference} className={styles.label}>{t('platform.invoices.paymentReference')}</label>
              <input
                id={ids.reference}
                className={`${styles.input} ${styles.mono}`}
                dir="ltr"
                value={form.reference}
                maxLength={120}
                onChange={(e) => setForm((f) => ({ ...f, reference: e.target.value }))}
              />
              <span className={styles.hint}>{t('platform.invoices.paymentReferenceHint')}</span>
            </div>
          </div>

          <div className={styles.formActions}>
            <Button type="submit" variant="primary" loading={busy}>{t('platform.invoices.recordPayment')}</Button>
          </div>
        </form>
      )}
    </section>
  );
}

// ── إشعارُ الدائن: التصحيحُ الوحيد الممكن على مستندٍ صدر ─────────────────────
function CreditNotesPanel({ invoice, money, onChanged }) {
  const { t } = useTranslation();
  const toast = useToast();
  const ids = { reason: useId(), description: useId(), amount: useId() };
  const [form, setForm] = useState({ reason: '', description: '', amount: '' });
  const [open, setOpen] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const problems = submitted
    ? {
      ...(form.reason.trim() ? {} : { reason: 'required' }),
      ...lineProblems({ description: form.description, quantity: 1, unitAmount: form.amount }),
    }
    : {};

  const issue = async (event) => {
    event.preventDefault();
    setSubmitted(true);
    const found = {
      ...(form.reason.trim() ? {} : { reason: 'required' }),
      ...lineProblems({ description: form.description, quantity: 1, unitAmount: form.amount }),
    };
    if (Object.keys(found).length > 0) return;
    setBusy(true); setError(null);
    try {
      const { number } = await api.issuePlatformCreditNote(invoice.id, {
        reason: form.reason.trim(),
        lines: [{
          description: form.description.trim(),
          quantity: 1,
          unitAmount: Number(form.amount),
          taxCategory: null,
        }],
      });
      setForm({ reason: '', description: '', amount: '' });
      setSubmitted(false);
      setOpen(false);
      await onChanged();
      toast.success(t('platform.invoices.creditIssued', { number }));
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.panel} aria-labelledby="invoice-credits">
      <h2 id="invoice-credits" className={styles.panelTitle}>{t('platform.invoices.creditNotes')}</h2>
      <p className={styles.panelHint}>{t('platform.invoices.creditNotesHint')}</p>

      {invoice.creditNotes.length > 0 && (
        <ul className={styles.rows}>
          {invoice.creditNotes.map((note) => (
            <li key={note.id} className={styles.row}>
              <span className={styles.rowMain}><span dir="ltr">{note.number}</span></span>
              <span className={styles.rowTags}>
                {note.issuedAtUtc && <>{formatDate(note.issuedAtUtc)} · </>}
                <strong>{money(note.total)}</strong>
                {note.reason && <> · {note.reason}</>}
              </span>
            </li>
          ))}
        </ul>
      )}

      {hasOutstanding(invoice) && !open && (
        <div className={styles.formActions}>
          <Button variant="secondary" onClick={() => setOpen(true)}>{t('platform.invoices.newCreditNote')}</Button>
        </div>
      )}

      {hasOutstanding(invoice) && open && (
        <form onSubmit={issue} noValidate>
          {error && <ErrorBanner message={error} />}
          <div className={styles.formGrid}>
            <div className={styles.field}>
              {/* السببُ مطلوب: إشعارٌ بلا سبب يُقرأ بعد سنتين ولا يُعرف لماذا خُفِّض مستحقّ. */}
              <label htmlFor={ids.reason} className={styles.label}>{t('platform.invoices.creditReason')}</label>
              <input
                id={ids.reason}
                className={styles.input}
                value={form.reason}
                maxLength={500}
                aria-invalid={!!problems.reason}
                onChange={(e) => setForm((f) => ({ ...f, reason: e.target.value }))}
              />
              {problems.reason && (
                <span className={styles.fieldError}>{t('platform.invoices.lineProblem.required')}</span>
              )}
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.description} className={styles.label}>{t('platform.invoices.lineDescription')}</label>
              <input
                id={ids.description}
                className={styles.input}
                value={form.description}
                maxLength={300}
                aria-invalid={!!problems.description}
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
              />
              {problems.description && (
                <span className={styles.fieldError}>{t(`platform.invoices.lineProblem.${problems.description}`)}</span>
              )}
            </div>

            <div className={styles.field}>
              <label htmlFor={ids.amount} className={styles.label}>{t('platform.invoices.creditAmount')}</label>
              <input
                id={ids.amount}
                className={`${styles.input} ${styles.mono}`}
                dir="ltr"
                type="number"
                step="0.001"
                min="0"
                value={form.amount}
                aria-invalid={!!problems.unitAmount}
                onChange={(e) => setForm((f) => ({ ...f, amount: e.target.value }))}
              />
              <span className={problems.unitAmount ? styles.fieldError : styles.hint}>
                {problems.unitAmount
                  ? t(`platform.invoices.lineProblem.${problems.unitAmount}`)
                  : t('platform.invoices.outstandingHint', { outstanding: money(invoice.outstanding) })}
              </span>
            </div>
          </div>

          <div className={styles.formActions}>
            <Button variant="secondary" onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
            <Button type="submit" variant="primary" loading={busy}>{t('platform.invoices.issueCreditNote')}</Button>
          </div>
        </form>
      )}
    </section>
  );
}
