import { NextResponse } from "next/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const maxDuration = 300;

type RouteContext = {
  params: Promise<{ path?: string[] }> | { path?: string[] };
};

const SMAPI_BASE_URL = process.env.SMAPI_BASE_URL ?? "http://localhost:5002/api";
const PROXY_TIMEOUT_MS = 300_000;
const HOP_BY_HOP_HEADERS = new Set([
  "connection",
  "content-length",
  "expect",
  "host",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
]);

export async function GET(request: Request, context: RouteContext) {
  return proxySmapiRequest(request, context);
}

export async function POST(request: Request, context: RouteContext) {
  return proxySmapiRequest(request, context);
}

export async function PUT(request: Request, context: RouteContext) {
  return proxySmapiRequest(request, context);
}

export async function PATCH(request: Request, context: RouteContext) {
  return proxySmapiRequest(request, context);
}

export async function DELETE(request: Request, context: RouteContext) {
  return proxySmapiRequest(request, context);
}

async function proxySmapiRequest(request: Request, context: RouteContext) {
  const params = await Promise.resolve(context.params);
  const path = params.path?.map(encodeURIComponent).join("/") ?? "";
  const incomingUrl = new URL(request.url);
  const targetUrl = `${SMAPI_BASE_URL.replace(/\/$/, "")}/${path}${incomingUrl.search}`;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), PROXY_TIMEOUT_MS);

  try {
    const response = await fetch(targetUrl, {
      method: request.method,
      headers: buildForwardHeaders(request.headers),
      body: hasRequestBody(request.method) ? await request.arrayBuffer() : undefined,
      cache: "no-store",
      signal: controller.signal,
    });

    const responseBody = await response.arrayBuffer();
    return new NextResponse(responseBody, {
      status: response.status,
      statusText: response.statusText,
      headers: buildResponseHeaders(response.headers),
    });
  } catch (error) {
    console.error("SMAPI proxy request failed", error);

    const detail = error instanceof Error ? error.message : "Unknown proxy error";
    const message = error instanceof Error && error.name === "AbortError"
      ? "SMAPI backend request timed out after 5 minutes."
      : `Could not connect to the SMAPI backend server. ${detail}`;

    return NextResponse.json({ success: false, message }, { status: 504 });
  } finally {
    clearTimeout(timeout);
  }
}

function buildForwardHeaders(headers: Headers) {
  const forwarded = new Headers();

  headers.forEach((value, key) => {
    if (!HOP_BY_HOP_HEADERS.has(key.toLowerCase())) {
      forwarded.set(key, value);
    }
  });

  return forwarded;
}

function buildResponseHeaders(headers: Headers) {
  const forwarded = new Headers();

  headers.forEach((value, key) => {
    if (!HOP_BY_HOP_HEADERS.has(key.toLowerCase())) {
      forwarded.set(key, value);
    }
  });

  return forwarded;
}

function hasRequestBody(method: string) {
  return method !== "GET" && method !== "HEAD";
}
