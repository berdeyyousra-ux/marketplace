# T-062 (T-083 "deployed alongside the API"): the Next.js client as a standalone Node server.
# Build from the repository root:
#   docker build -f deploy/docker/Web.Dockerfile --build-arg COVER_IMAGE_BASE_URL=https://<account>.blob.core.windows.net/product-covers/ .
# COVER_IMAGE_BASE_URL is read when next.config.ts is evaluated at build time (next/image's allowed origin), so it is
# a build argument. API_BASE_URL is read at run time and comes from the container's environment.
FROM node:22-bookworm-slim AS build
WORKDIR /web
ENV NEXT_TELEMETRY_DISABLED=1
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
ARG COVER_IMAGE_BASE_URL
ENV COVER_IMAGE_BASE_URL=${COVER_IMAGE_BASE_URL}
RUN npm run build

FROM node:22-bookworm-slim AS runtime
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
# The server runs on node alone. npm and corepack ship with the base image but are never used here, and they carry
# their own dependencies (and advisories), so the runtime image drops them.
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/corepack \
    /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack
WORKDIR /app
COPY --from=build --chown=node:node /web/.next/standalone ./
COPY --from=build --chown=node:node /web/.next/static ./.next/static
COPY --from=build --chown=node:node /web/public ./public
USER node
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD node -e "fetch('http://localhost:3000/').then(r => process.exit(r.status < 500 ? 0 : 1)).catch(() => process.exit(1))"
CMD ["node", "server.js"]
