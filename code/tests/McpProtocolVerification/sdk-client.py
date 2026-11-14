# Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
"""Independent official Python MCP SDK interoperability check, against a running fish with its MCP interface on
(BigBlueFish.Mcp without --pipe, as external clients such as the Hermes bridge connect)."""
import asyncio,json,sys
from mcp import ClientSession
from mcp.client.stdio import stdio_client, StdioServerParameters

async def main():
    async with stdio_client(StdioServerParameters(command=sys.argv[1])) as (receive,send):
        async with ClientSession(receive,send) as session:
            init=await session.initialize()
            assert init.server_info.name=='BigBlueFish'
            tools=await session.list_tools()
            assert len(tools.tools)==17
            assert {'get_context','walk_to','find_on_screen','request_local_speech','get_operation_guide'} <= {t.name for t in tools.tools}
            settings=await session.call_tool('get_settings',{})
            assert not settings.is_error
            actual=json.loads(settings.content[0].text)
            assert actual['settings']['McpEnabled']
            actions=await session.call_tool('list_actions',{})
            assert len(json.loads(actions.content[0].text))==106
            text='主人，這是透過獨立 MCP SDK 傳來的原文呀~'
            said=await session.call_tool('say',{'text':text})
            assert json.loads(said.content[0].text)['text']==text
            image=await session.call_tool('capture_screen',{'max_dimension':1280})
            assert image.content[1].type=='image' and image.content[1].mime_type=='image/png'
            denied=await session.call_tool('update_settings',{'settings':{'McpEnabled':False}})
            assert denied.is_error
            denied=await session.call_tool('update_settings',{'settings':{'AgentEnabled':True}})
            assert denied.is_error
            assert 'RemoteApiKeyProtected' not in actual['settings'] and 'CwaApiKeyProtected' not in actual['settings']
            await session.send_ping()
            print('Official Python MCP SDK: initialize, 17 tools, settings without secrets, 106 actions, literal speech, PNG image, protected switches and ping passed.')

asyncio.run(main())

