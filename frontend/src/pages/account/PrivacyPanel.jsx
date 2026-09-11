import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import FormField, { inputClass } from '../../components/common/FormField';
import PasswordInput from '../../components/common/PasswordInput';
import Button from '../../components/common/Button';
import { downloadJson } from '../../features/account/download';
import styles from './Account.module.css';

// الخصوصية (المرحلة 7): تنزيل كل بيانات العميل (قابلية النقل) وحذف الحساب نهائياً. الحذف يتطلّب كلمة المرور — الخادم
// يتحقّق منها ويمحو ويُلغي الجلسات، ثم تخرج الواجهة محلياً (رمز التجديد أُلغي على الخادم أصلاً).
export default function PrivacyPanel() {
  const { t } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const { logout } = useAuth();
  const [exporting, setExporting] = useState(false);
  const [password, setPassword] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [erasing, setErasing] = useState(false);
  const [eraseError, setEraseError] = useState(null);

  const exportData = async () => {
    setExporting(true);
    try { downloadJson(await api.exportMyData(), 'account-data.json'); }
    catch (e) { toast.error(e.message); }
    finally { setExporting(false); }
  };

  const erase = async (e) => {
    e.preventDefault();
    setSubmitted(true);
    if (!password) return;
    setErasing(true); setEraseError(null);
    try {
      await api.eraseMyAccount(password);
      logout();
      toast.success(t('account.erased'));
      navigate('/', { replace: true });
    } catch (err) { setEraseError(err.message); setErasing(false); }
  };

  const passwordMissing = submitted && !password;

  return (
    <section className={styles.panel}>
      <div className={styles.panelHead}><h2 className={styles.panelTitle}>{t('account.privacyTitle')}</h2></div>

      <div className={styles.privacyBlock}>
        <h3>{t('account.exportTitle')}</h3>
        <p className={styles.muted}>{t('account.exportDesc')}</p>
        <Button variant="ghost" loading={exporting} onClick={exportData}>{t('account.exportButton')}</Button>
      </div>

      <form className={`${styles.privacyBlock} ${styles.danger}`} onSubmit={erase} noValidate>
        <h3>{t('account.eraseTitle')}</h3>
        <p className={styles.muted}>{t('account.eraseDesc')}</p>
        <FormField label={t('account.erasePasswordLabel')} htmlFor="erase-password"
          error={passwordMissing ? t('account.passwordRequired') : eraseError}>
          <PasswordInput id="erase-password" value={password} autoComplete="current-password"
            className={inputClass(passwordMissing || !!eraseError)} onChange={(e) => setPassword(e.target.value)} />
        </FormField>
        <Button type="submit" variant="danger" loading={erasing}>{t('account.eraseButton')}</Button>
      </form>
    </section>
  );
}
