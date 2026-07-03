import { NextResponse } from "next/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const maxDuration = 1800;

const SMAPI_BASE_URL = process.env.SMAPI_BASE_URL ?? "http://localhost:5002/api";
const ALLOWED_TYPES = new Set(["db-only", "full"]);
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

export async function GET(request: Request) {
  const cookieHeader = request.headers.get("cookie") ?? "";
  if (!cookieHeader.includes("auth_token=")) {
    return NextResponse.json(
      { success: false, message: "Login is required before downloading backups." },
      { status: 401 }
    );
  }

  const incomingUrl = new URL(request.url);
  const type = incomingUrl.searchParams.get("type") ?? "db-only";
  if (!ALLOWED_TYPES.has(type)) {
    return NextResponse.json(
      { success: false, message: "Backup type must be db-only or full." },
      { status: 400 }
    );
  }

  const targetUrl = `${SMAPI_BASE_URL.replace(/\/$/, "")}/Backup/download?type=${encodeURIComponent(type)}`;
  const response = await fetch(targetUrl, {
    method: "GET",
    cache: "no-store",
  });

  return new NextResponse(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: buildResponseHeaders(response.headers),
  });
}

function buildResponseHeaders(headers: Headers) {
  const forwarded = new Headers();

  headers.forEach((value, key) => {
    if (!HOP_BY_HOP_HEADERS.has(key.toLowerCase())) {
      forwarded.set(key, value);
    }
  });

  forwarded.set("Cache-Control", "no-store");
  return forwarded;
}
