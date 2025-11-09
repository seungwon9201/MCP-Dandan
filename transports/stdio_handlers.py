"""
STDIO transport verification API handlers.

These endpoints are used by the CLI proxy for verifying tool calls and responses.
"""

import json
from aiohttp import web

from state import state
from verification import verify_tool_call, verify_tool_response


async def handle_verify_request(request):
    """
    Handle verification request for a tool call from STDIO proxy.

    POST /verify/request

    Body:
    {
        "message": {...},  // JSON-RPC message
        "toolName": "...",
        "serverInfo": {...}
    }

    Returns:
    {
        "blocked": bool,
        "reason": str,
        "modified": bool
    }
    """
    try:
        data = await request.json()
    except Exception as e:
        return web.Response(
            status=400,
            text=json.dumps({"error": "Invalid JSON"}),
            content_type='application/json'
        )

    message = data.get('message')
    tool_name = data.get('toolName')

    if not message or not tool_name:
        return web.Response(
            status=400,
            text=json.dumps({"error": "Missing required fields"}),
            content_type='application/json'
        )

    try:
        # Extract args from message
        tool_args = message.get('params', {}).get('arguments', {})

        # Extract user_intent and remove from args
        user_intent = tool_args.get('user_intent', '')
        tool_args_clean = {k: v for k, v in tool_args.items() if k != 'user_intent'}

        # Server info
        server_info = data.get('serverInfo', {})
        print(f"[Verify] Tool call: {tool_name} from {server_info.get('appName')}/{server_info.get('name')}")

        if user_intent:
            print(f"[Verify] User intent: {user_intent}")

        # Verify the tool call
        verification = await verify_tool_call(
            tool_name=tool_name,
            tool_args=tool_args_clean,
            server_info=server_info,
            user_intent=user_intent
        )

        return web.Response(
            status=200,
            text=json.dumps({
                "blocked": not verification.allowed,
                "reason": verification.reason if not verification.allowed else None,
                "modified": False
            }),
            content_type='application/json'
        )

    except Exception as e:
        print(f"[Verify] Error verifying tool call: {e}")
        return web.Response(
            status=500,
            text=json.dumps({"error": "Verification error", "blocked": True}),
            content_type='application/json'
        )


async def handle_verify_response(request):
    """
    Handle verification request for a tool response from STDIO proxy.

    POST /verify/response

    Body:
    {
        "message": {...},  // JSON-RPC response message
        "toolName": "...",
        "serverInfo": {...}
    }

    Returns:
    {
        "blocked": bool,
        "reason": str,
        "modified": bool
    }
    """
    try:
        data = await request.json()
    except Exception as e:
        return web.Response(
            status=400,
            text=json.dumps({"error": "Invalid JSON"}),
            content_type='application/json'
        )

    message = data.get('message')
    tool_name = data.get('toolName')

    if not message or not tool_name:
        return web.Response(
            status=400,
            text=json.dumps({"error": "Missing required fields"}),
            content_type='application/json'
        )

    try:
        server_info = data.get('serverInfo', {})
        print(f"[Verify] Tool response: {tool_name} from {server_info.get('appName')}/{server_info.get('name')}")

        # Verify the tool response
        verification = await verify_tool_response(
            tool_name=tool_name,
            response_data=message,
            server_info=server_info
        )

        return web.Response(
            status=200,
            text=json.dumps({
                "blocked": not verification.allowed,
                "reason": verification.reason if not verification.allowed else None,
                "modified": False
            }),
            content_type='application/json'
        )

    except Exception as e:
        print(f"[Verify] Error verifying response: {e}")
        return web.Response(
            status=500,
            text=json.dumps({"error": "Verification error", "blocked": False}),
            content_type='application/json'
        )


async def handle_register_tools(request):
    """
    Handle tool registration from CLI proxy.

    POST /register-tools

    Body:
    {
        "tools": [...],
        "serverInfo": {...},
        "appName": "...",
        "serverName": "..."
    }

    Returns:
    {
        "success": bool,
        "message": str,
        "stats": {...}
    }
    """
    try:
        data = await request.json()
    except Exception as e:
        return web.Response(
            status=400,
            text=json.dumps({"error": "Invalid JSON"}),
            content_type='application/json'
        )

    tools = data.get('tools')
    app_name = data.get('appName')
    server_name = data.get('serverName')
    server_info = data.get('serverInfo', {})

    if not tools or not isinstance(tools, list):
        return web.Response(
            status=400,
            text=json.dumps({"error": "Invalid tools data"}),
            content_type='application/json'
        )

    try:
        print(f"[Register] Registering {len(tools)} tools for {app_name}/{server_name}")

        # Log tool information
        for i, tool in enumerate(tools):
            description = tool.get('description', '(no description)')
            print(f"  {i+1}. {tool.get('name')} - {description}")

        # Register tools in state
        await state.register_tools(
            app_name=app_name,
            server_name=server_name,
            tools=tools,
            server_info=server_info
        )

        # Count tools with/without descriptions
        with_desc = sum(1 for t in tools if t.get('description'))
        without_desc = len(tools) - with_desc

        print(f"[Register] Successfully registered {len(tools)} tools for {app_name}:{server_name}")
        print(f"  {with_desc} with descriptions, {without_desc} without")

        return web.Response(
            status=200,
            text=json.dumps({
                "success": True,
                "message": f"Registered {len(tools)} tools for {app_name}:{server_name}",
                "stats": {
                    "total": len(tools),
                    "withDescriptions": with_desc,
                    "withoutDescriptions": without_desc
                }
            }),
            content_type='application/json'
        )

    except Exception as e:
        print(f"[Register] Error registering tools: {e}")
        return web.Response(
            status=500,
            text=json.dumps({"error": "Tool registration error"}),
            content_type='application/json'
        )
