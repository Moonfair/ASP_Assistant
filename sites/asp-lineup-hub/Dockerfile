FROM node:24-bookworm-slim AS build
WORKDIR /app
COPY package.json scripts/build.mjs ./
COPY web ./web
COPY server ./server
RUN mkdir scripts && mv build.mjs scripts/build.mjs && node scripts/build.mjs

FROM node:24-bookworm-slim
ENV NODE_ENV=production
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund && npm cache clean --force \
    && install -d -m 0700 -o node -g node /data
COPY --from=build /app/dist ./dist
COPY scripts/serve.mjs scripts/local-db.mjs scripts/mail.mjs scripts/feedback-storage.mjs ./scripts/
COPY drizzle ./drizzle
USER node
EXPOSE 4173
CMD ["node", "scripts/serve.mjs"]
