import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../app/queryKeys';
import { useTenant } from '../../app/TenantProvider';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import DataTable from '../../components/common/DataTable';
import RowActionsMenu from '../../components/common/RowActionsMenu';
import { useConfirmAction } from '../../components/common/useConfirmAction';
import { ErrorBanner } from '../../components/common/StateViews';
import Skeleton from '../../components/common/Skeleton';
import { formatPrice } from '../../components/product/ProductBadges';
import { AlertIcon, ChevronIcon } from '../../components/icons/Icons';
import { localizedName } from '../../features/catalog/catalogText';
import { hiddenFromStorefront, variantLabel } from '../../features/admin/products/variantModel';
import ProductOptionsEditor from './ProductOptionsEditor';
import CreateVariantsPanel from './CreateVariantsPanel';
import EditVariantDrawer from './EditVariantDrawer';
import { AdjustStockDrawer, StockMovementDrawer } from './StockDrawers';
import adminStyles from './Admin.module.css';
import styles from './ProductVariants.module.css';

const STATUS_STYLE = { Active: 'delivered', Draft: 'pending', Archived: 'cancelled' };

// ============================================================================
// خيارات منتج ومتغيّراته (catalog.manage، ADR-0040): صفحة بمسار (/admin/products/:id/variants) لا درج — المصفوفة تحتاج
// مساحة، والرابط يُعاد تحميله فيبقى المدير حيث كان. نموذج المنتج من الخادم (TanStack Query) مصدر كل شيء: بعد أي حفظ
// يُبطَل ويُقرأ من جديد، فلا حالة محلية تفترق عمّا حُفظ. المخزون للعرض وتصحيحه بدرجَي الجرد نفسيهما (وحدة Inventory).
// ============================================================================
export default function ProductVariants() {
  const { productId } = useParams();
  const { t, i18n } = useTranslation();
  const lang = i18n.language?.startsWith('en') ? 'en' : 'ar';
  const tenant = useTenant();
  const defaultCulture = tenant.config?.settings?.locale?.defaultCulture ?? 'ar';
  const { can } = useAuth();
  const toast = useToast();
  const confirmation = useConfirmAction();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(null);
  const [adjusting, setAdjusting] = useState(null);
  const [historyFor, setHistoryFor] = useState(null);

  const { data: product, error, isPending, refetch } = useQuery({
    queryKey: queryKeys.adminProduct(productId),
    queryFn: () => api.getAdminProduct(productId),
  });

  const reload = () => queryClient.invalidateQueries({ queryKey: queryKeys.adminProduct(productId) });

  if (isPending) {
    return (
      <div className={styles.page}>
        <Skeleton height={32} width="40%" />
        <Skeleton height={220} />
        <Skeleton height={260} />
      </div>
    );
  }
  if (error) return <ErrorBanner message={error.message} onRetry={refetch} />;

  const productName = localizedName(product, lang) || product.slug;
  const labelOf = (variant) => variantLabel(product.options, variant.optionValueIds, lang) || t('admin.variants.noLabel');
  const titleOf = (variant) => t('admin.inventory.itemName', { name: productName, variant: labelOf(variant) });
  const activeCount = product.variants.filter((v) => v.isActive).length;

  const setActive = (variant, isActive) => {
    const run = async () => {
      await api.setProductVariantStatus(product.id, variant.id, isActive);
      toast.success(t(isActive ? 'admin.variants.variantActivated' : 'admin.variants.variantDeactivated'));
      await reload();
    };
    // التعطيل يُؤكَّد دائماً، والتفعيل حين يجعل المنتج مخفياً من الواجهة (متغيّر نشط ثانٍ).
    if (!isActive || activeCount === 1) {
      confirmation.ask({
        title: t(isActive ? 'admin.variants.confirmActivate.title' : 'admin.variants.confirmDeactivate.title', { name: labelOf(variant) }),
        message: t(isActive ? 'admin.variants.confirmActivate.message' : 'admin.variants.confirmDeactivate.message'),
        confirmLabel: t(isActive ? 'admin.variants.confirmActivate.action' : 'admin.variants.confirmDeactivate.action'),
        danger: !isActive,
        action: run,
      });
      return;
    }
    run().catch((err) => toast.error(err.message));
  };

  const makeDefault = async (variant) => {
    try {
      await api.setDefaultProductVariant(product.id, variant.id);
      toast.success(t('admin.variants.defaultChanged'));
      await reload();
    } catch (err) { toast.error(err.message); }
  };

  const saveVariant = async (payload) => {
    await api.updateProductVariant(product.id, editing.id, payload);
    toast.success(t('admin.variants.variantUpdated'));
    setEditing(null);
    await reload();
  };

  const actionsFor = (variant) => [
    { label: t('admin.variants.edit'), onClick: () => setEditing(variant) },
    variant.isActive
      ? { label: t('admin.variants.deactivate'), variant: 'danger', disabled: variant.isDefault, onClick: () => setActive(variant, false) }
      : { label: t('admin.variants.activate'), onClick: () => setActive(variant, true) },
    ...(variant.isDefault ? [] : [{ label: t('admin.variants.makeDefault'), disabled: !variant.isActive, onClick: () => makeDefault(variant) }]),
    ...(can('inventory.manage') ? [{ label: t('admin.variants.adjustStock'), onClick: () => setAdjusting(variant) }] : []),
    ...(can('inventory.view') ? [{ label: t('admin.variants.stockHistory'), onClick: () => setHistoryFor(variant) }] : []),
  ];

  const columns = [
    {
      key: 'variant', header: t('admin.variants.colVariant'), render: (v) => (
        <div className={styles.variantCell}>
          <span className={styles.variantName} dir="auto">{labelOf(v)}</span>
          {v.isDefault && <span className={styles.defaultBadge} title={t('admin.variants.defaultHint')}>{t('admin.variants.default')}</span>}
        </div>
      ),
    },
    { key: 'sku', header: t('admin.variants.colSku'), width: '130px', truncate: true, tooltip: (v) => v.sku, render: (v) => <span dir="ltr">{v.sku ?? '—'}</span> },
    {
      key: 'price', header: t('admin.variants.colPrice'), width: '120px', align: 'end', render: (v) => (
        <div>
          <div>{formatPrice(v.price, product.currency)}</div>
          {v.compareAtPrice != null && <div className={adminStyles.nameSecondary}><s>{formatPrice(v.compareAtPrice, product.currency)}</s></div>}
        </div>
      ),
    },
    {
      key: 'stock', header: t('admin.variants.colStock'), width: '150px', align: 'end',
      render: (v) => <span className={adminStyles.nameSecondary}>{t('admin.variants.stockLevel', { available: v.available, onHand: v.onHand })}</span>,
    },
    {
      key: 'status', header: t('admin.variants.colStatus'), width: '96px', render: (v) => (
        <span className={`${adminStyles.statusBadge} ${v.isActive ? adminStyles.delivered : adminStyles.cancelled}`}>
          {t(v.isActive ? 'admin.variants.active' : 'admin.variants.inactive')}
        </span>
      ),
    },
    {
      key: 'actions', header: t('admin.variants.colActions'), width: '64px', align: 'end',
      render: (v) => <RowActionsMenu actions={actionsFor(v)} label={`${t('admin.variants.colActions')}: ${labelOf(v)}`} />,
    },
  ];

  return (
    <div className={styles.page}>
      <Link to="/admin/products" className={styles.back}><ChevronIcon dir="start" size={14} /> {t('admin.variants.back')}</Link>
      <div className={styles.titleRow}>
        <div>
          <h2 className={adminStyles.pageTitle}>{t('admin.variants.title')}</h2>
          <p className={styles.productLine}>
            <span dir="auto">{productName}</span>
            <span className={`${adminStyles.statusBadge} ${adminStyles[STATUS_STYLE[product.status]] ?? ''}`}>
              {t(`admin.products.status.${product.status}`)}
            </span>
          </p>
        </div>
      </div>
      <p className={adminStyles.pageSub}>{t('admin.variants.subtitle')}</p>

      {hiddenFromStorefront(product.variants) && (
        <div className={styles.notice} role="status">
          <AlertIcon size={18} />
          <span>{t('admin.variants.storefrontHidden')}</span>
        </div>
      )}

      <ProductOptionsEditor key={JSON.stringify(product.options)} product={product} defaultCulture={defaultCulture} lang={lang} onSaved={reload} />

      <section className={styles.card} aria-labelledby="variants-title">
        <header className={styles.cardHead}>
          <div>
            <h3 id="variants-title" className={styles.cardTitle}>
              {t('admin.variants.variantsTitle')} <span className={styles.count}>{product.variants.length}</span>
            </h3>
            <p className={styles.cardHint}>{t('admin.variants.variantsHint')}</p>
          </div>
        </header>
        <DataTable columns={columns} rows={product.variants} rowKey={(v) => v.id} minWidth="720px" stickyFirstColumn />
      </section>

      <CreateVariantsPanel key={`${product.variants.length}-${JSON.stringify(product.options)}`} product={product} lang={lang} onCreated={reload} />

      {editing && (
        <EditVariantDrawer variant={editing} title={labelOf(editing)} skuMaxLength={product.variantLimits.skuMaxLength}
          onSave={saveVariant} onClose={() => setEditing(null)} />
      )}
      {adjusting && (
        <AdjustStockDrawer item={{ ...adjusting, variantId: adjusting.id }} title={titleOf(adjusting)}
          onClose={() => setAdjusting(null)} onDone={() => { setAdjusting(null); reload(); }} />
      )}
      {historyFor && (
        <StockMovementDrawer variantId={historyFor.id} title={titleOf(historyFor)} onClose={() => setHistoryFor(null)} />
      )}
      {confirmation.dialog}
    </div>
  );
}
