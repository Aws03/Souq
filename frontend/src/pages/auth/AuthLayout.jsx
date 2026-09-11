import { ErrorBanner } from '../../components/common/StateViews';
import StoreBrand from '../../app/StoreBrand';
import styles from './Auth.module.css';

// غلاف مشترك لشاشتي الدخول والتسجيل: خلفية بلون المتجر + بطاقة بيضاء + اسمه أو شعاره من إعداده (المرحلة 15؛ على مضيف المنصّة
// اسم المنصّة).
export default function AuthLayout({ title, subtitle, serverError, children }) {
  return (
    <div className={styles.wrap}>
      <div className={styles.card}>
        <div className={styles.brand}><StoreBrand /></div>
        <h2 className={styles.title}>{title}</h2>
        <p className={styles.subtitle}>{subtitle}</p>
        {serverError && <ErrorBanner message={serverError} />}
        {children}
      </div>
    </div>
  );
}
