import { Component } from 'react';
import { withTranslation } from 'react-i18next';
import styles from './StateViews.module.css';

// ============================================================================
// آخر خطّ دفاع في الواجهة: خطأ عرض غير متوقّع في أي شجرة أسفل هذا الحدّ يوقف React عن
// عرض التطبيق كلّه — فيرى الزائر صفحة بيضاء. هذا الحدّ يحوّلها إلى شاشة يمكن التعافي منها.
//
// ما لا يُعرَض للزائر أبداً: نصّ الاستثناء ومكدّسه. هما في وحدة التحكّم للمطوّر وحده؛ عرضهما
// يسرّب أسماء داخلية ومسارات ملفّات بلا فائدة لأحد (نفس قاعدة عقد الأخطاء في الخادم).
//
// صنف لا دالّة: componentDidCatch لا مقابل له في الخطّافات حتى اليوم.
// ============================================================================
class ErrorBoundaryBase extends Component {
  constructor(props) {
    super(props);
    this.state = { failed: false };
  }

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error, info) {
    // للمطوّر في المتصفّح فقط. لا مخدّم تجميع أخطاء في هذا المشروع بعد (ADR-0018).
    console.error('[storefront] render failure', error, info?.componentStack);
  }

  // إعادة المحاولة تمسح حالة الفشل: خطأ عابر (طلب فشل أثناء العرض) يختفي، والدائم يعود فوراً.
  retry = () => this.setState({ failed: false });

  render() {
    const { t, children } = this.props;
    if (!this.state.failed) return children;

    return (
      <div className={styles.boundary} role="alert">
        <h1 className={styles.boundaryTitle}>{t('errors.boundaryTitle')}</h1>
        <p className={styles.boundaryMsg}>{t('errors.boundaryMessage')}</p>
        <div className={styles.boundaryActions}>
          <button type="button" className={styles.boundaryPrimary} onClick={this.retry}>
            {t('common.retry')}
          </button>
          <a className={styles.boundarySecondary} href="/">{t('errors.boundaryHome')}</a>
        </div>
      </div>
    );
  }
}

export default withTranslation()(ErrorBoundaryBase);
