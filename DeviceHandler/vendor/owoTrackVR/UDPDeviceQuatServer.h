#pragma once

#include "NetworkedDeviceQuatServer.h"
#include "Network.h"
#include "ByteBuffer.h"

using namespace bb;

class UDPDeviceQuatServer : public NetworkedDeviceQuatServer
{
private:
	std::function<void(std::wstring, int32_t)> Log;

	uint32_t* portno; // Port number pointer
	WSASession Session;
	UDPSocket Socket;

	// Per-tracker client addresses
	sockaddr_in clients[MAX_TRACKERS];
	unsigned long long lastContactTime[MAX_TRACKERS];
	bool connectionIsDead[MAX_TRACKERS];

	void send_heartbeat();

	char* buffer;

	bool more_data_exists__read();

	unsigned long long curr_time = 0;

	int hb_accum;

	void send_bytebuffer(ByteBuffer& b, int trackerId);

	// Find or assign a tracker ID for a client address
	int getOrAssignTrackerId(const sockaddr_in& addr);
	bool isSameClient(const sockaddr_in& a, const sockaddr_in& b);

public:
	UDPDeviceQuatServer(uint32_t* portno_v, std::function<void(std::wstring, int32_t)> loggerFunction);

	void startListening(bool& _ret) override;
	void tick() override;

	bool isConnectionAlive(int trackerId) override;

	void buzz(int trackerId, float duration_s, float frequency, float amplitude) override;

	int get_port() override;
};
</Parameter>
<parameter name="Complexity">5
