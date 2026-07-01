import asyncio
import websockets

clients = set()

async def handler(websocket):
    clients.add(websocket)
    print("Client Connected")

    try:
        async for message in websocket:

            dead = []

            for client in clients:
                if client != websocket:
                    try:
                        await client.send(message)
                    except:
                        dead.append(client)

            for d in dead:
                clients.remove(d)

    except websockets.ConnectionClosed:
        pass

    clients.remove(websocket)
    print("Client Disconnected")


async def main():
    async with websockets.serve(handler, "0.0.0.0", 8080):
        print("Server Running")
        await asyncio.Future()


asyncio.run(main())