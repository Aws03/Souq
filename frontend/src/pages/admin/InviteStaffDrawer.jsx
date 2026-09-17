import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Drawer from '../../components/common/Drawer';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import { ErrorBanner } from '../../components/common/StateViews';
import { STAFF_ROLES, buildInvitePayload, inviteProblems, inviteToForm } from '../../features/admin/staff/staffView';
import styles from './CategoryFormDrawer.module.css';
import settingsStyles from '../../components/settings/StoreSettingsEditor.module.css';

// درج دعوة عضو في فريق المتجر: الاسم والبريد والدور. الدور يُشرح بما يستطيعه لا باسمه وحده — "مدير"
// يعني الإعدادات والفريق والمدفوعات، وهو ما يجب أن يعرفه من يمنحه قبل أن يمنحه.
export default function InviteStaffDrawer({ onInvite, onClose }) {
  const { t } = useTranslation();
  const [form, setForm] = useState(inviteToForm);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const problems = submitted ? inviteProblems(form) : {};
  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    setSubmitted(true);
    if (Object.keys(inviteProblems(form)).length > 0) return;
    setBusy(true); setError(null);
    try { await onInvite(buildInvitePayload(form)); }
    catch (err) { setError(err.message); setBusy(false); }
  };

  return (
    <Drawer open onClose={onClose} side="right" busy={busy} title={t('admin.staff.inviteTitle')}
      footer={
        <div className={styles.footActions}>
          <Button variant="ghost" onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
          <Button variant="primary" type="submit" form="invite-staff-form" loading={busy}>{t('admin.staff.sendInvite')}</Button>
        </div>
      }>
      <form id="invite-staff-form" noValidate onSubmit={submit}>
        {error && <ErrorBanner message={error} />}
        <p className={settingsStyles.sectionHint}>{t('admin.staff.inviteHint')}</p>

        <FormField label={t('admin.staff.nameLabel')} htmlFor="invite-name"
          error={problems.fullName && t(`admin.staff.problem.${problems.fullName}`)}>
          <input id="invite-name" className={inputClass(!!problems.fullName)} value={form.fullName}
            onChange={set('fullName')} autoComplete="off" aria-invalid={!!problems.fullName} />
        </FormField>

        <FormField label={t('admin.staff.emailLabel')} htmlFor="invite-email"
          error={problems.email && t(`admin.staff.problem.${problems.email}`)}>
          <input id="invite-email" type="email" dir="ltr" className={inputClass(!!problems.email)} value={form.email}
            onChange={set('email')} autoComplete="off" aria-invalid={!!problems.email} />
        </FormField>

        <fieldset className={settingsStyles.fieldset}>
          <legend className={settingsStyles.label}>{t('admin.staff.roleLabel')}</legend>
          {STAFF_ROLES.map((role) => (
            <label key={role} className={settingsStyles.roleOption}>
              <input type="radio" name="role" value={role} checked={form.role === role} onChange={set('role')} />
              <span>
                <b>{t(`admin.staff.role.${role}`)}</b>
                <span className={settingsStyles.hint}>{t(`admin.staff.roleHint.${role}`)}</span>
              </span>
            </label>
          ))}
        </fieldset>
      </form>
    </Drawer>
  );
}
