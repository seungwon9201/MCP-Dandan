"""
82ch - Unified MCP Security Framework

Single entry point for Observer (MCP Proxy) + Engine (Threat Detection).
Runs both components in a single process.
"""

import os
import sys
import asyncio
from aiohttp import web

# Observer components
from transports.sse_transport import handle_sse_connection
from transports.message_handler import handle_message_endpoint
from transports.http_only_handler import handle_http_only_message
from transports.stdio_handlers import (
    handle_verify_request,
    handle_verify_response,
    handle_register_tools
)
from state import state
from config import config

# Engine components
from database import Database
from event_hub import EventHub
from engines.sensitive_file_engine import SensitiveFileEngine
from engines.tools_poisoning_engine import ToolsPoisoningEngine
from engines.command_injection_engine import CommandInjectionEngine
from engines.file_system_exposure_engine import FileSystemExposureEngine


def setup_engines(db: Database) -> list:
    """Initialize and configure detection engines based on config."""
    engines = []

    # Sensitive File Engine
    if config.get_sensitive_file_enabled():
        engine = SensitiveFileEngine(db)
        engines.append(engine)

    # Tools Poisoning Engine (LLM-based)
    if config.get_tools_poisoning_enabled():
        engine = ToolsPoisoningEngine(db, detail_mode=True)
        engines.append(engine)

    # Command Injection Engine
    if config.get_command_injection_enabled():
        engine = CommandInjectionEngine(db)
        engines.append(engine)

    # File System Exposure Engine
    if config.get_file_system_exposure_enabled():
        engine = FileSystemExposureEngine(db)
        engines.append(engine)

    return engines


async def initialize_engine_system():
    """Initialize the engine detection system."""
    print("=" * 80)
    print("Initializing Engine System")
    print("=" * 80)

    # Initialize database
    db = Database()
    await db.connect()

    # Setup engines
    engines = setup_engines(db)

    if engines:
        print(f"\nActive Detection Engines ({len(engines)}):")
        for i, engine in enumerate(engines, 1):
            print(f"  {i}. {engine.name}")
    else:
        print("\nWarning: No detection engines enabled!")

    # Initialize EventHub
    event_hub = EventHub(engines, db)
    await event_hub.start()

    # Store in global state
    state.event_hub = event_hub

    print("\nEngine system initialized successfully")
    print("=" * 80)

    return db, event_hub


async def handle_health(request):
    """Health check endpoint."""
    return web.Response(
        text='{"status": "ok", "components": ["observer", "engine"]}',
        content_type='application/json'
    )


def setup_routes(app):
    """Setup application routes."""

    # Health check
    app.router.add_get('/health', handle_health)

    # STDIO verification API endpoints
    app.router.add_post('/verify/request', handle_verify_request)
    app.router.add_post('/verify/response', handle_verify_response)
    app.router.add_post('/register-tools', handle_register_tools)

    # HTTP+SSE transport endpoints
    # Format: /{appName}/{serverName}/sse (GET)
    app.router.add_get('/{app}/{server}/sse', handle_sse_connection)

    # Format: /{appName}/{serverName}/message (POST)
    app.router.add_post('/{app}/{server}/message', handle_message_endpoint)

    # HTTP-only transport endpoint (no SSE)
    # Format: /{appName}/{serverName}/mcp (POST)
    app.router.add_post('/{app}/{server}/mcp', handle_http_only_message)

    print(f"\n[Server] Routes configured:")
    print(f"  GET  /health - Health check")
    print(f"  POST /verify/request - STDIO verification API")
    print(f"  POST /verify/response - STDIO verification API")
    print(f"  POST /register-tools - Tool registration")
    print(f"  GET  /{{app}}/{{server}}/sse - SSE connection endpoint")
    print(f"  POST /{{app}}/{{server}}/message - Message endpoint")
    print(f"  POST /{{app}}/{{server}}/mcp - HTTP-only endpoint (no SSE)")


async def on_startup(app):
    """Called when the application starts."""
    state.running = True

    print("\n" + "=" * 80)
    print("82ch - MCP Security Framework")
    print("=" * 80)
    print("Observer + Engine integrated mode")
    print("=" * 80)

    # Initialize engine system
    db, event_hub = await initialize_engine_system()

    # Store references for cleanup
    app['db'] = db
    app['event_hub'] = event_hub

    print(f"\n[Observer] Starting HTTP server...")
    print(f"[Observer] Listening on http://{config.server_host}:{config.server_port}")
    print(f"[Observer] Scan mode: {config.scan_mode}")
    print("\n" + "=" * 80)
    print("All components ready. Waiting for connections...")
    print("Press Ctrl+C to stop")
    print("=" * 80 + "\n")


async def on_shutdown(app):
    """Called when the application shuts down."""
    state.running = False

    print(f"\n[Server] Shutting down...")

    # Stop EventHub
    if state.event_hub:
        await state.event_hub.stop()

    # Close database
    if 'db' in app:
        await app['db'].close()

    print(f"[Server] All components stopped")


def create_app():
    """Create and configure the aiohttp application."""
    app = web.Application()

    # Setup routes
    setup_routes(app)

    # Setup lifecycle callbacks
    app.on_startup.append(on_startup)
    app.on_shutdown.append(on_shutdown)

    return app


def main():
    """Main entry point."""
    app = create_app()

    # Run the server
    web.run_app(
        app,
        host=config.server_host,
        port=config.server_port,
        print=None  # Disable aiohttp's startup message
    )


if __name__ == '__main__':
    try:
        # Windows asyncio event loop policy
        if sys.platform == 'win32':
            asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

        main()
    except KeyboardInterrupt:
        print("\n[Server] Shutting down...")
        sys.exit(0)