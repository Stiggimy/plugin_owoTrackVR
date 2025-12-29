#include "pch.h"

#include "UDPDeviceQuatServer.h"
#include "ByteBuffer.h"

#include <ctime>

using namespace bb;

void UDPDeviceQuatServer::send_heartbeat()
{
	hb_accum += 1;

	if (hb_accum > 200)
	{
		hb_accum = 0;

		// Send heartbeat to all connected trackers
		for (int i = 0; i < activeTrackerCount; i++)
		{
			if (!isConnectionAlive(i))
				continue;

			ByteBuffer buff(sizeof(int) * 2);
			buff.putInt(1);
			buff.putInt(0);

			send_bytebuffer(buff, i);
		}
	}
}


void UDPDeviceQuatServer::send_bytebuffer(ByteBuffer& b, int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return;
	
	auto buff_c = static_cast<uint8_t*>(malloc(b.size()));
	b.getBytes(buff_c, b.size());

	Socket.SendTo(clients[trackerId], (char*)buff_c, b.size());

	free(buff_c);
}

bool UDPDeviceQuatServer::isSameClient(const sockaddr_in& a, const sockaddr_in& b)
{
	return a.sin_addr.s_addr == b.sin_addr.s_addr && a.sin_port == b.sin_port;
}

int UDPDeviceQuatServer::getOrAssignTrackerId(const sockaddr_in& addr)
{
	// Check if this client already has an ID
	for (int i = 0; i < activeTrackerCount; i++)
	{
		if (isSameClient(clients[i], addr))
		{
			return i;
		}
	}

	// Assign new ID if we have space
	if (activeTrackerCount < MAX_TRACKERS)
	{
		int newId = activeTrackerCount;
		clients[newId] = addr;
		trackers[newId].isConnected = true;
		trackers[newId].lastContactTime = curr_time;
		connectionIsDead[newId] = false;
		lastContactTime[newId] = curr_time;
		activeTrackerCount++;
		
		Log(std::format(L"New owoTrack device connected! Tracker ID: {}", newId), 0);
		return newId;
	}

	// No space for new trackers
	return -1;
}

UDPDeviceQuatServer::UDPDeviceQuatServer(uint32_t* portno_v,
                                         std::function<void(std::wstring, int32_t)> loggerFunction) :
	NetworkedDeviceQuatServer(), Log(loggerFunction), Socket(loggerFunction)
{
	buffer = static_cast<char*>(malloc(sizeof(char) * MAX_MSG_SIZE));

	portno = portno_v;

	// Initialize all client slots
	for (int i = 0; i < MAX_TRACKERS; i++)
	{
		clients[i] = {0};
		lastContactTime[i] = 0;
		connectionIsDead[i] = true;
	}
}


void UDPDeviceQuatServer::startListening(bool& _ret)
{
	_ret = Socket.Bind(portno);
}

bool UDPDeviceQuatServer::more_data_exists__read()
{
	// read header
	curr_time = static_cast<unsigned long long>(std::time(nullptr));

	sockaddr_in incomingClient = {0};
	const bool is_recv = Socket.RecvFrom(buffer, MAX_MSG_SIZE, reinterpret_cast<SOCKADDR*>(&incomingClient));
	if (!is_recv) return false;

	const message_header_type_t msg_type = convert_chars<message_header_type_t>((unsigned char*)buffer);

	// Handle handshake before assigning tracker ID
	if (msg_type == MSG_HANDSHAKE)
	{
		// Respond with hello message - client will be assigned an ID on first data packet
		Socket.SendTo(incomingClient, buff_hello, buff_hello_len);
		return true;
	}

	// Get or assign tracker ID for this client
	int trackerId = getOrAssignTrackerId(incomingClient);
	if (trackerId < 0)
	{
		// No space for new trackers
		return true;
	}

	// Update connection status
	lastContactTime[trackerId] = curr_time;
	connectionIsDead[trackerId] = false;
	trackers[trackerId].isConnected = true;
	trackers[trackerId].lastContactTime = curr_time;

	switch (msg_type)
	{
	case MSG_HEARTBEAT:
		return true;
	case MSG_ROTATION:
		handle_rotation_packet((unsigned char*)buffer, trackerId);
		return true;
	case MSG_GYRO:
		handle_gyro_packet((unsigned char*)buffer, trackerId);
		return true;
	case MSG_ACCELEROMETER:
		handle_accel_packet((unsigned char*)buffer, trackerId);
		return true;
	default:
		return true;
	}
	return true;
}

void UDPDeviceQuatServer::tick()
{
	send_heartbeat();
	while (more_data_exists__read())
	{
	}

	// Check for dead connections
	for (int i = 0; i < activeTrackerCount; i++)
	{
		if (!connectionIsDead[i] && (curr_time - lastContactTime[i]) > 2)
		{
			connectionIsDead[i] = true;
			trackers[i].isConnected = false;
			Log(std::format(L"owoTrack device {} disconnected (timeout)", i), 1);
		}
	}
}

bool UDPDeviceQuatServer::isConnectionAlive(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return false;
	return !connectionIsDead[trackerId];
}

void UDPDeviceQuatServer::buzz(int trackerId, float duration_s, float frequency, float amplitude)
{
	if (trackerId < 0 || trackerId >= activeTrackerCount) return;
	if (!isConnectionAlive(trackerId)) return;

	ByteBuffer buff(sizeof(int) + sizeof(float) * 3);
	buff.putInt(2);
	buff.putFloat(duration_s);
	buff.putFloat(frequency);
	buff.putFloat(amplitude);

	send_bytebuffer(buff, trackerId);
}

int UDPDeviceQuatServer::get_port()
{
	return *portno;
}
