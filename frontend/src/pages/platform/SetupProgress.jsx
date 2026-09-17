import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { SETUP_STEPS } from '../../features/platform/provisioning';
import styles from './Platform.module.css';

// شريط خطوات التجهيز. الهوية خطوة منجزة متى وُجد المتجر؛ ما بعدها روابط حرّة — كل خطوة تُحفظ وحدها، فلا ترتيب
// إجباري يحبس المالك خلف خطوة لا يملك قرارها الآن (نطاق العميل لم يُشترَ بعد مثلاً). aria-current على الحالية.
export default function SetupProgress({ current, storeId = null }) {
  const { t } = useTranslation();
  const steps = ['identity', ...SETUP_STEPS];
  return (
    <nav aria-label={t('platform.setup.progressLabel')}>
      <ol className={styles.progress}>
        {steps.map((step, index) => {
          const isCurrent = step === current;
          const done = storeId !== null && step === 'identity';
          const label = (
            <>
              <span className={styles.progressIndex} aria-hidden="true">{done ? '✓' : index + 1}</span>
              <span>{t(`platform.setup.step.${step}`)}</span>
            </>
          );
          const className = `${styles.progressStep} ${isCurrent ? styles.progressCurrent : ''} ${done ? styles.progressDone : ''}`;
          return (
            <li key={step} className={className}>
              {storeId !== null && step !== 'identity' && !isCurrent
                ? <Link to={`/platform/stores/${storeId}/setup/${step}`}>{label}</Link>
                : <span aria-current={isCurrent ? 'step' : undefined}>{label}</span>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
