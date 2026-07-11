import { ErrorBanner } from '../../components/common/StateViews';
import styles from './Auth.module.css';

// غلاف مشترك لشاشتي الدخول والتسجيل: خلفية بترولية + بطاقة بيضاء + شعار.
export default function AuthLayout({ title, subtitle, serverError, children }) {
  return (
    <div className={styles.wrap}>
      <div className={styles.card}>
        <div className={styles.brand}>Mar<span>ka</span></div>
        <h2 className={styles.title}>{title}</h2>
        <p className={styles.subtitle}>{subtitle}</p>
        {serverError && <ErrorBanner message={serverError} />}
        {children}
      </div>
    </div>
  );
}
