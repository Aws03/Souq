import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useModule, useStoreConfig } from '../../app/TenantProvider';
import StoreBrand, { useStoreName } from '../../app/StoreBrand';
import { pickText } from '../../app/tenantModel';
import { FacebookIcon, InstagramIcon, XIcon, PhoneIcon, MailIcon, MapPinIcon } from '../icons/Icons';
import styles from './Footer.module.css';

const SOCIAL_ICONS = { facebook: FacebookIcon, instagram: InstagramIcon, x: XIcon };

// تذييل المتجر (المرحلة 15، A4): الهوية ووصفها وروابط الشبكات وبيانات التواصل كلها من إعداد المتجر — لا اسم ولا هاتف ولا بريد
// مكتوب هنا. ما لا يضبطه المتجر لا يُرسم. يظهر أسفل صفحات المتجر (لا لوحة الإدارة).
export default function Footer() {
  const { t, i18n } = useTranslation();
  const config = useStoreConfig();
  const name = useStoreName();
  const wishlist = useModule('wishlist');
  const settings = config?.settings;
  const culture = settings?.locale?.defaultCulture;
  const description = pickText(settings?.seo?.description, i18n.language, culture);
  const address = pickText(settings?.contact?.address, i18n.language, culture);
  const { email, phone } = settings?.contact ?? {};
  const social = (settings?.social ?? []).filter((link) => link.url);
  const year = new Date().getFullYear();

  return (
    <footer className={styles.footer}>
      <div className={`souq-layout ${styles.footer__grid}`}>
        <div className={styles.footer__col}>
          <StoreBrand className={styles.footer__brand} />
          {description && <p className={styles.footer__desc}>{description}</p>}
          {social.length > 0 && (
            <div className={styles.footer__social}>
              {social.map(({ network, url }) => {
                const Icon = SOCIAL_ICONS[network];
                return (
                  <a key={network} href={url} target="_blank" rel="noopener noreferrer" aria-label={network}>
                    {Icon ? <Icon size={16} /> : network}
                  </a>
                );
              })}
            </div>
          )}
        </div>

        <div className={styles.footer__col}>
          <h4 className={styles.footer__heading}>{t('footer.quickLinks')}</h4>
          <nav className={styles.footer__links}>
            <Link to="/">{t('footer.home')}</Link>
            {wishlist && <Link to="/wishlist">{t('nav.wishlistAria')}</Link>}
            <Link to="/login">{t('nav.login')}</Link>
            <Link to="/register">{t('nav.registerFull')}</Link>
          </nav>
        </div>

        <div className={styles.footer__col}>
          <h4 className={styles.footer__heading}>{t('footer.customerService')}</h4>
          <nav className={styles.footer__links}>
            <a href="#">{t('footer.faq')}</a>
            <a href="#">{t('footer.shipping')}</a>
            <a href="#">{t('footer.returns')}</a>
            <a href="#">{t('footer.contactLink')}</a>
          </nav>
        </div>

        {(phone || email || address) && (
          <div className={styles.footer__col}>
            <h4 className={styles.footer__heading}>{t('footer.contactUs')}</h4>
            <ul className={styles.footer__contact}>
              {phone && <li><PhoneIcon size={14} /><a href={`tel:${phone}`} dir="ltr">{phone}</a></li>}
              {email && <li><MailIcon size={14} /><a href={`mailto:${email}`} dir="ltr">{email}</a></li>}
              {address && <li><MapPinIcon size={14} />{address}</li>}
            </ul>
          </div>
        )}
      </div>

      <div className={styles.footer__bottom}>
        <div className={`souq-layout ${styles.footer__bottomInner}`}>
          <span>{t('footer.copyright', { year, store: name })}</span>
          <div className={styles.footer__legal}>
            <a href="#">{t('footer.privacyPolicy')}</a>
            <a href="#">{t('footer.terms')}</a>
          </div>
        </div>
      </div>
    </footer>
  );
}
