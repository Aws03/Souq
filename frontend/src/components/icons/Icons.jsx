// مجموعة أيقونات SVG بسيطة (بلا إيموجي إطلاقاً) — كل أيقونة مكوّن صغير مستقل
// يرث اللون الحالي (currentColor) كي يتماشى مع أي سياق (أزرار، شارات، تنبيهات).
const base = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round', strokeLinejoin: 'round' };

export const SearchIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" />
  </svg>
);

export const CartIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <circle cx="9" cy="21" r="1.4" /><circle cx="18" cy="21" r="1.4" />
    <path d="M2.5 3h2l2.4 12.2a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.6L21 7H6" />
  </svg>
);

export const CameraIcon = ({ size = 28 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M4 8h3l1.6-2.2h6.8L17 8h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1Z" />
    <circle cx="12" cy="13.5" r="3.4" />
  </svg>
);

export const CloseIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}><path d="M5 5l14 14M19 5 5 19" /></svg>
);

export const TrashIcon = ({ size = 16 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M4 7h16M9 7V4.8A.8.8 0 0 1 9.8 4h4.4a.8.8 0 0 1 .8.8V7M6 7l1 13a1 1 0 0 0 1 .9h8a1 1 0 0 0 1-.9l1-13" />
  </svg>
);

export const ChevronIcon = ({ size = 16, dir = 'down' }) => {
  const rotate = { down: 0, up: 180, start: 90, end: -90 }[dir];
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" style={{ transform: `rotate(${rotate}deg)` }} {...base}>
      <path d="m6 9 6 6 6-6" />
    </svg>
  );
};

export const CheckIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}><path d="m5 13 4 4L19 7" /></svg>
);

export const MenuIcon = ({ size = 22 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}><path d="M4 6h16M4 12h16M4 18h16" /></svg>
);

export const UserIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <circle cx="12" cy="8" r="3.4" /><path d="M5 20c1.2-3.6 4-5.4 7-5.4s5.8 1.8 7 5.4" />
  </svg>
);

export const MoreIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor">
    <circle cx="12" cy="5" r="1.7" /><circle cx="12" cy="12" r="1.7" /><circle cx="12" cy="19" r="1.7" />
  </svg>
);

export const TruckIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M2 6h11v10H2zM13 10h4l4 3.2V16h-8z" />
    <circle cx="6.5" cy="18" r="1.6" /><circle cx="17" cy="18" r="1.6" />
  </svg>
);

export const PlusIcon = ({ size = 14 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}><path d="M12 5v14M5 12h14" /></svg>
);

export const MinusIcon = ({ size = 14 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}><path d="M5 12h14" /></svg>
);

export const AlertIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M12 3 2 20h20L12 3Z" /><path d="M12 10v4M12 17h.01" />
  </svg>
);

export const InfoIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" />
  </svg>
);

export const SuccessIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <circle cx="12" cy="12" r="9" /><path d="m8 12.5 2.5 2.5L16 9.5" />
  </svg>
);

export const PackageIcon = ({ size = 56 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="m3.5 7.5 8.5-4 8.5 4-8.5 4-8.5-4Z" /><path d="M3.5 7.5v9l8.5 4 8.5-4v-9" /><path d="M12 11.5v9" />
  </svg>
);

export const CardIcon = ({ size = 20 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <rect x="2.5" y="5" width="19" height="14" rx="2" /><path d="M2.5 10h19M6 15h4" />
  </svg>
);

export const GridIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <rect x="3" y="3" width="8" height="8" rx="1.5" /><rect x="13" y="3" width="8" height="8" rx="1.5" />
    <rect x="3" y="13" width="8" height="8" rx="1.5" /><rect x="13" y="13" width="8" height="8" rx="1.5" />
  </svg>
);

export const TagIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M11.5 3.5 20 12l-8 8-8.5-8.5V3.5h8Z" /><circle cx="8" cy="8" r="1.3" fill="currentColor" stroke="none" />
  </svg>
);

export const ReceiptIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3Z" /><path d="M9 8h6M9 12h6" />
  </svg>
);

export const StarIcon = ({ size = 16, filled = false }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth={1.8} strokeLinejoin="round">
    <path d="m12 3 2.7 5.9 6.3.7-4.7 4.4 1.3 6.3L12 17.3 6.4 20.3l1.3-6.3-4.7-4.4 6.3-.7L12 3Z" />
  </svg>
);

export const PercentIcon = ({ size = 18 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M5 19 19 5" /><circle cx="7" cy="7" r="2.3" /><circle cx="17" cy="17" r="2.3" />
  </svg>
);

export const RefreshIcon = ({ size = 16 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" {...base}>
    <path d="M4 12a8 8 0 0 1 14-5.2M20 12a8 8 0 0 1-14 5.2" /><path d="M18 3v4h-4M6 21v-4h4" />
  </svg>
);
