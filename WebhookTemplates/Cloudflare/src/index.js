function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status, headers: { "content-type": "application/json; charset=utf-8" }
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/health") {
      if (request.headers.get("authorization") !== `Bearer ${env.API_KEY}`)
        return json({ error: "unauthorized" }, 401);
      return json({ ok: true, service: "N-Remo Realtime Relay", version: 2 });
    }
    if (url.pathname === "/ws" ||
        (request.method === "POST" &&
         url.pathname === `/webhook/${env.WEBHOOK_SECRET}`)) {
      return env.RELAY.getByName("n-remo").fetch(request);
    }
    return json({ error: "not found" }, 404);
  }
};

export class NRemoRelay {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (request.method === "POST") return this.receiveWebhook(request);
    if (url.pathname === "/ws") return this.openWebSocket(request, url);
    return json({ error: "not found" }, 404);
  }

  async receiveWebhook(request) {
    let body;
    try { body = await request.json(); }
    catch { return json({ error: "invalid json" }, 400); }
    const context = body.context ?? {};
    const deviceMac = context.deviceMac ?? context.deviceId ?? "";
    if (!deviceMac) return json({ error: "device id is missing" }, 400);

    const id = (await this.ctx.storage.get("nextId") ?? 1);
    const event = {
      id, receivedAt: new Date().toISOString(), provider: "SwitchBot",
      deviceMac, eventType: body.eventType ?? "changeReport", context
    };
    await this.ctx.storage.put(`event:${String(id).padStart(16, "0")}`, event);
    await this.ctx.storage.put("nextId", id + 1);

    const old = await this.ctx.storage.list({ prefix: "event:", reverse: true });
    const obsolete = [...old.keys()].slice(1000);
    if (obsolete.length) await this.ctx.storage.delete(obsolete);
    const text = JSON.stringify(event);
    for (const socket of this.ctx.getWebSockets()) {
      try { socket.send(text); } catch { }
    }
    return json({ ok: true, id });
  }

  async openWebSocket(request, url) {
    if (request.headers.get("Upgrade") !== "websocket")
      return json({ error: "websocket upgrade required" }, 426);
    if (request.headers.get("authorization") !== `Bearer ${this.env.API_KEY}`)
      return json({ error: "unauthorized" }, 401);
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    this.ctx.acceptWebSocket(server);

    const after = Number(url.searchParams.get("after") ?? "0") || 0;
    const stored = await this.ctx.storage.list({ prefix: "event:" });
    for (const event of stored.values())
      if (event.id > after) server.send(JSON.stringify(event));
    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage() { }
  webSocketClose(socket, code, reason) { socket.close(code, reason); }
  webSocketError() { }
}
