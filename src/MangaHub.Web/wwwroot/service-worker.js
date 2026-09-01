const CACHE_NAME = "mangahub-app-v199";
const APP_SHELL = [
  "/",
  "/index.html",
  "/offline.html",
  "/manifest.webmanifest",
  "/icons/book.svg",
  "/css/app.css?v=207",
  "/MangaHub.Web.styles.css?v=207"
];

self.addEventListener("install", event => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(APP_SHELL))
      .catch(() => undefined)
  );
});

self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", event => {
  const request = event.request;

  if (request.method !== "GET") {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith("/api/")) {
    return;
  }

  event.respondWith(
    fetch(request)
      .then(response => {
        if (request.mode === "navigate" && response.status >= 500) {
          return caches.match("/offline.html");
        }
        if (!response.ok) {
          return response;
        }
        const copy = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
        return response;
      })
      .catch(() => caches.match(request).then(response => response || (request.mode === "navigate" ? caches.match("/offline.html") : caches.match("/index.html"))))
  );
});

self.addEventListener("push", event => {
  const payload = event.data ? event.data.json() : {};
  event.waitUntil(self.registration.showNotification(payload.title || "MangaHub", {
    body: payload.body || "A new chapter is available.", icon: "/icon-192.png", badge: "/icon-192.png", data: { url: payload.url || "/library" }
  }));
});

self.addEventListener("notificationclick", event => {
  event.notification.close();
  const targetUrl = new URL(event.notification.data?.url || "/library", self.location.origin).href;
  event.waitUntil(
    clients.matchAll({ type: "window", includeUncontrolled: true }).then(windows => {
      const existingWindow = windows.find(windowClient => new URL(windowClient.url).origin === self.location.origin);
      if (!existingWindow) {
        return clients.openWindow(targetUrl);
      }

      return existingWindow.navigate(targetUrl)
        .catch(() => existingWindow)
        .then(windowClient => (windowClient || existingWindow).focus());
    })
  );
});
