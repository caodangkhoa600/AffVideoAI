// The container's health check: answers when the web app itself is serving,
// whatever the state of the API behind it.
export function GET() {
  return Response.json({ status: "ok" });
}
