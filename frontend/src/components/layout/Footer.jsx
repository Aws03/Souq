import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { FacebookIcon, InstagramIcon, XIcon, PhoneIcon, MailIcon, MapPinIcon } from '../icons/Icons';
import styles from './Footer.module.css';

// تذييل موقع بأربعة أعمدة (هوية + روابط سريعة + خدمة عملاء + تواصل) وشريط
// سفلي لحقوق النشر — يظهر أسفل كل صفحات المتجر (لا لوحة الإدارة).
export default function Footer() {
  const { t } = useTranslation();
  const year = new Date().getFullYear();

  return (
    <footer className={styles.footer}>
      <div className={`souq-layout ${styles.footer__grid}`}>
        <div className={styles.footer__col}>
          <div className={styles.footer__brand}>Mar<span>ka</span></div>
          <p className={styles.footer__desc}>{t('footer.brandDesc')}</p>
          <div className={styles.footer__social}>
            <a href="#" aria-label="Facebook"><FacebookIcon size={16} /></a>
            <a href="#" aria-label="Instagram"><InstagramIcon size={16} /></a>
            <a href="#" aria-label="X"><XIcon size={16} /></a>
          </div>
        </div>

        <div className={styles.footer__col}>
          <h4 className={styles.footer__heading}>{t('footer.quickLinks')}</h4>
          <nav className={styles.footer__links}>
            <Link to="/">{t('footer.home')}</Link>
            <Link to="/wishlist">{t('nav.wishlistAria')}</Link>
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

        <div className={styles.footer__col}>
          <h4 className={styles.footer__heading}>{t('footer.contactUs')}</h4>
          <ul className={styles.footer__contact}>
            <li><PhoneIcon size={14} /><span dir="ltr">+962 6 000 0000</span></li>
            <li><MailIcon size={14} /><span dir="ltr">support@marka.example</span></li>
            <li><MapPinIcon size={14} />{t('footer.address')}</li>
          </ul>
        </div>
      </div>

      <div className={styles.footer__bottom}>
        <div className={`souq-layout ${styles.footer__bottomInner}`}>
          <span>{t('footer.copyright', { year })}</span>
          <div className={styles.footer__legal}>
            <a href="#">{t('footer.privacyPolicy')}</a>
            <a href="#">{t('footer.terms')}</a>
          </div>
        </div>
      </div>
    </footer>
  );
}
