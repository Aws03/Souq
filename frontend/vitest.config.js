import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// ============================================================================
// بيئتان في مجموعة واحدة (TD-32): معظم الاختبارات منطق خالص لا يحتاج DOM ويعمل أسرع بلا
// jsdom، واختبارات المكوّنات تحتاجه. بدل فرض jsdom على الجميع — وإبطاء 110 اختبارات لأجل
// عشرات — تُحدَّد البيئة بالتعليق // @vitest-environment jsdom في أعلى ملف المكوّن.
// ============================================================================
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'node',
    setupFiles: ['./src/test/setup.js'],
    globals: false,
    css: false,
  },
});
