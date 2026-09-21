import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useModule, useStoreConfig } from '../../app/TenantProvider';
import StoreBrand, { useStoreName } from '../../app/StoreBrand';
import { pickText } from '../../app/tenantModel';
import {
  FacebookIcon, InstagramIcon, XIcon, TiktokIcon, YoutubeIcon, SnapchatIcon, LinkedinIcon, WhatsappIcon,
  PhoneIcon, MailIcon, MapPinIcon,
} from '../icons/Icons';
import styles from './Footer.module.css';

// كل شبكة يعرضها الخادم في `SocialLink.Networks` لها أيقونة هنا. الثلاث الأولى وحدها كانت موجودة،
// فمتجرٌ يضيف تيك‑توك كان يرى كلمة "tiktok" بحروف صغيرة بين الأيقونات. يحرس التطابقَ اختبارُ معمارية
// يقرأ هذا الملفّ ويقارنه بقائمة النطاق — إضافة شبكة تاسعة بلا أيقونة تُسقط البناء.
export const SOCIAL_ICONS = {
  facebook: FacebookIcon,
  instagram: InstagramIcon,
  x: XIcon,
  tiktok: TiktokIcon,
  youtube: YoutubeIcon,
  snapchat: SnapchatIcon,
  linkedin: LinkedinIcon,
  whatsapp: WhatsappIcon,
};

// أنواع السياسات التي يقبلها الخادم (`StorePolicyLinks.Kinds`) ⇒ مفتاح ترجمة اسمها. الرابط نفسه من
// إعداد المتجر، والاسم شيفرةٌ هنا كما الأيقونة — فنوعٌ يُضاف في النطاق بلا اسم هنا يُرسم مفتاحاً
// لاتينياً خامّاً في تذييل عربي. يحرس التطابقَ اختبار معمارية، كما يحرس أيقونات الشبكات.
export const POLICY_LABELS = {
  privacy: 'footer.policies.privacy',
  terms: 'footer.policies.terms',
  returns: 'footer.policies.returns',
  shipping: 'footer.policies.shipping',
  faq: 'footer.policies.faq',
};

// تذييل المتجر (المرحلة 15، A4): الهوية ووصفها وروابط الشبكات وبيانات التواصل كلها من إعداد المتجر — لا اسم ولا هاتف ولا بريد
// مكتوب هنا. ما لا يضبطه المتجر لا يُرسم. يظهر أسفل صفحات المتجر (لا لوحة الإدارة).
//
// المرحلة 16: حُذف عمودا "خدمة العملاء" و"السياسات" — ستّة روابط <a href="#"> لا تذهب إلى شيء
// (أسئلة شائعة، شحن، إرجاع، تواصل، سياسة خصوصية، شروط). المنصّة لا تملك صفحات محتوى للمتجر
// أصلاً، فالروابط كانت زينة تُوهم المشتري بوجود صفحة. الغياب أصدق من رابط ميّت، والنقص مسجّل
// في سجلّ الدين (صفحات محتوى المتجر).
//
// TD-42، قرار المالك C (2026-09-21): **روابط الآن**. عمود السياسات يعود — لكن بروابط يضبطها
// التاجر لصفحات يستضيفها هو، ويُرسم النوع المضبوط وحده. فالعمود كلّه يغيب عن متجر لم يضبط شيئاً،
// وهو فرق جوهري عن الرابط الميّت: ما يظهر يذهب إلى صفحة فعلاً.
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
  // النوع المضبوط وحده، بترتيب POLICY_LABELS لا بترتيب ردّ الخادم — كي لا يتبدّل ترتيب التذييل.
  const policies = Object.keys(POLICY_LABELS)
    .map((kind) => ({ kind, url: settings?.policies?.[kind] }))
    .filter(({ url }) => url);
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

        {policies.length > 0 && (
          <div className={styles.footer__col}>
            <h4 className={styles.footer__heading}>{t('footer.policies.heading')}</h4>
            <nav className={styles.footer__links}>
              {policies.map(({ kind, url }) => (
                <a key={kind} href={url} target="_blank" rel="noopener noreferrer">{t(POLICY_LABELS[kind])}</a>
              ))}
            </nav>
          </div>
        )}

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
        </div>
      </div>
    </footer>
  );
}
