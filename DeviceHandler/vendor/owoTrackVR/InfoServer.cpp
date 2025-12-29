#include "pch.h"
#include "InfoServer.h"

bool InfoServer::respond_to_all_requests()
{
	sockaddr_in addr;
	const bool is_recv = Socket.RecvFrom(buff, MAX_BUFF_SIZE, reinterpret_cast<SOCKADDR*>(&addr));
	if (!is_recv) return false;

	if (strcmp(buff, "DISCOVERY\0") == 0)
	{
		Socket.SendTo(addr, response_info.c_str(), response_info.length());
	}
	return true;
}

InfoServer::InfoServer(bool& _ret, std::function<void(std::wstring, int32_t)> loggerFunction) :
	Log(loggerFunction), Socket(loggerFunction)
{
	buff = static_cast<char*>(malloc(MAX_BUFF_SIZE));
	_ret = Socket.Bind(&INFO_PORT);
	update_response_info();
}

void InfoServer::set_tracker_count(int count)
{
	trackerCount = count;
	update_response_info();
}

void InfoServer::update_response_info()
{
	// Build response with all available tracker slots
	// Format: "port:name\n" for each tracker
	response_info = "";
	for (int i = 0; i < trackerCount; i++)
	{
		response_info += std::to_string(port_no) + ":Tracker " + std::to_string(i) + "\n";
	}
}

void InfoServer::tick()
{
	while (respond_to_all_requests())
	{
	}
}
