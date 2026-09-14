# ============================================================================
# صورة الواجهة: مرحلة بناء (Vite) ثم تقديم الناتج الساكن عبر Nginx مع توكيل
# عكسي لـ /api و/uploads نحو حاوية الـ API (nginx.conf). لا حاجة لضبط CORS في
# الخادم لهذا المسار: المتصفح يرى أصلاً واحداً فقط (Nginx)، والتوكيل داخلي.
# ============================================================================
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
