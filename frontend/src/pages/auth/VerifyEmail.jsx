import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useAuth } from '../../context/AuthContext';
import Button from '../../components/common/Button';
import Spinner from '../../components/common/Spinner';
import AuthLayout from './AuthLayout';
import styles from './Auth.module.css';

// تأكيد البريد — تُفتح من رابط الرسالة (?token=...) فتُرسل الرمز تلقائياً مرّة واحدة: الرمز للاستخدام
// مرّة، والاستدعاء المزدوج للتأثيرات في StrictMode كان سيُظهر خطأً زائفاً بعد نجاح حقيقي (لذا ref).
// رابط منتهٍ/مستخدَم لمستخدم مسجّل ⇒ زرّ لطلب رابط جديد.
export default function VerifyEmail() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const { isAuthenticated, reloadUser } = useAuth();
  const [status, setStatus] = useState(token ? 'pending' : 'missing');
  const [serverError, setServerError] = useState(null);
  const [resend, setResend] = useState('idle');   // idle | busy | sent
  const submitted = useRef(false);

  useEffect(() => {
    if (!token || submitted.current) return;
    submitted.current = true;
    api.verifyEmail(token)
      .then(() => setStatus('done'))
      .catch((err) => { setStatus('failed'); setServerError(err.message); });
  }, [token]);

  // المستخدم المسجّل يرى حالته الجديدة (استعادة الجلسة قد تنتهي بعد التأكيد، لذا نعتمد على الاثنين).
  useEffect(() => {
    if (status === 'done' && isAuthenticated) reloadUser().catch(() => {});
  }, [status, isAuthenticated, reloadUser]);

  const requestNewLink = async () => {
    setResend('busy'); setServerError(null);
    try {
      await api.resendVerification();
      setResend('sent');
    } catch (err) {
      setServerError(err.message);
      setResend('idle');
    }
  };

  return (
    <AuthLayout title={t('auth.verifyEmailTitle')} subtitle={t('auth.verifyEmailSubtitle')} serverError={serverError}>
      {status === 'pending' && <p className={styles.switch}><Spinner /> {t('auth.verifyEmailPending')}</p>}
      {status === 'missing' && <p className={styles.successBox}>{t('auth.invalidVerifyLink')}</p>}
      {status === 'done' && <p className={styles.successBox}>{t('auth.verifyEmailSuccess')}</p>}
      {status === 'failed' && isAuthenticated && (resend === 'sent'
        ? <p className={styles.successBox}>{t('auth.verificationSent')}</p>
        : (
          <Button type="button" variant="accent" size="lg" loading={resend === 'busy'} className={styles.submit}
            onClick={requestNewLink}>
            {t('auth.resendVerification')}
          </Button>
        ))}
      {status === 'failed' && !isAuthenticated && (
        <p className={styles.switch}><Link to="/login">{t('auth.signIn')}</Link></p>
      )}
      <p className={styles.switch}><Link to="/">{t('auth.verifyEmailContinue')}</Link></p>
    </AuthLayout>
  );
}
