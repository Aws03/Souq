import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import FormField, { inputClass } from '../../components/common/FormField';
import Button from '../../components/common/Button';
import styles from './Account.module.css';

// البيانات الشخصية: الاسم والهاتف. البريد بريد الدخول ولا يتغيّر من هنا. onSave لا يرمي — المتصل يعرض الخطأ.
export default function ProfileForm({ profile, onSave }) {
  const { t } = useTranslation();
  const [fullName, setFullName] = useState(profile.fullName);
  const [phone, setPhone] = useState(profile.phone ?? '');
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const nameMissing = !fullName.trim();

  const submit = async (e) => {
    e.preventDefault();
    setSubmitted(true);
    if (nameMissing) return;
    setBusy(true);
    await onSave({ fullName: fullName.trim(), phone: phone.trim() || null });
    setBusy(false);
  };

  return (
    <form className={styles.panel} onSubmit={submit} noValidate>
      <div className={styles.panelHead}><h2 className={styles.panelTitle}>{t('account.profileTitle')}</h2></div>

      <FormField label={t('account.fullNameLabel')} htmlFor="profile-name" error={submitted && nameMissing && t('account.nameRequired')}>
        <input id="profile-name" className={inputClass(submitted && nameMissing)} value={fullName}
          onChange={(e) => setFullName(e.target.value)} autoComplete="name" />
      </FormField>

      <FormField label={t('account.emailLabel')} htmlFor="profile-email" hint={t('account.emailHint')}>
        <input id="profile-email" className={inputClass(false)} value={profile.email} dir="ltr" readOnly />
      </FormField>

      <FormField label={t('account.phoneLabel')} htmlFor="profile-phone">
        <input id="profile-phone" className={inputClass(false)} value={phone} type="tel" dir="ltr"
          onChange={(e) => setPhone(e.target.value)} autoComplete="tel" />
      </FormField>

      <Button type="submit" variant="primary" loading={busy}>{t('account.saveProfile')}</Button>
    </form>
  );
}
