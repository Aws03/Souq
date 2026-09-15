import Spinner from './Spinner';
import styles from './Button.module.css';

/**
 * زر عام موحّد لكل الواجهة. variant يحدّد الشكل، loading يعرض دوّارة بدل النص
 * ويمنع النقر المتكرر أثناء الإرسال (لا حاجة لتعطيل خارجي مكرر).
 */
export default function Button({
  variant = 'primary', size = 'md', loading = false, disabled = false,
  type = 'button', className = '', children, ...rest
}) {
  return (
    <button
      type={type}
      className={`${styles.btn} ${styles[variant]} ${styles[size]} ${className}`}
      disabled={disabled || loading}
      aria-busy={loading}
      {...rest}
    >
      {loading && <Spinner size={size === 'sm' ? 14 : 16} inverted={variant === 'primary' || variant === 'accent'} />}
      <span className={loading ? styles.hiddenLabel : ''}>{children}</span>
    </button>
  );
}
