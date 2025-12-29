#pragma once

#include "Network.h"
#include "DeviceQuatServer.h"
#include <vector>

class InfoServer
{
	std::function<void(std::wstring, int32_t)> Log;

	uint32_t INFO_PORT = 35903;
	uint32_t port_no = 6969;

	UDPSocket Socket;

	char* buff;
	const int MAX_BUFF_SIZE = 64;

	std::string response_info;

	bool respond_to_all_requests();

	// Reference to data server for tracker count
	int trackerCount = MAX_TRACKERS;

public:
	InfoServer(bool& _ret, std::function<void(std::wstring, int32_t)> loggerFunction);

	void set_tracker_count(int count);
	void tick();

	void set_port_no(const uint32_t& new_port_no)
	{
		port_no = new_port_no;
		update_response_info();
	}

private:
	void update_response_info();
};
