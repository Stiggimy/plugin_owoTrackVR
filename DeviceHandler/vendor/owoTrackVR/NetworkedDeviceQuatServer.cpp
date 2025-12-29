#include "pch.h"
#include "NetworkedDeviceQuatServer.h"
#include <stdlib.h>

void NetworkedDeviceQuatServer::handle_doubles_packet(unsigned char* packet, double* into, int num_doubles, TrackerData& tracker)
{
	packet += sizeof(message_header_type_t);

	const message_id_t id = convert_chars<message_id_t>(packet);
	packet += sizeof(message_id_t);
	if (!tracker.receive_packet_id(id)) return;

	for (int i = 0; i < num_doubles; i++)
	{
		const sensor_data_t data = convert_chars<sensor_data_t>(packet);
		packet += sizeof(sensor_data_t);
		into[i] = static_cast<double>(data);
	}

	tracker.isNewDataAvailable = true;
}


void NetworkedDeviceQuatServer::handle_gyro_packet(unsigned char* packet, int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return;
	handle_doubles_packet(packet, trackers[trackerId].gyro_buffer, 3, trackers[trackerId]);
}

void NetworkedDeviceQuatServer::handle_rotation_packet(unsigned char* packet, int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return;
	handle_doubles_packet(packet, trackers[trackerId].quat_buffer, 4, trackers[trackerId]);
}

void NetworkedDeviceQuatServer::handle_accel_packet(unsigned char* packet, int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return;
	handle_doubles_packet(packet, trackers[trackerId].accel_buffer, 3, trackers[trackerId]);
}


int NetworkedDeviceQuatServer::getActiveTrackerCount()
{
	return activeTrackerCount;
}

bool NetworkedDeviceQuatServer::isTrackerConnected(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return false;
	return trackers[trackerId].isConnected;
}

bool NetworkedDeviceQuatServer::isDataAvailable(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return false;
	const bool was_available = trackers[trackerId].isNewDataAvailable;
	trackers[trackerId].isNewDataAvailable = false;
	return was_available;
}

double* NetworkedDeviceQuatServer::getRotationQuaternion(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return nullptr;
	return trackers[trackerId].quat_buffer;
}

double* NetworkedDeviceQuatServer::getGyroscope(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return nullptr;
	return trackers[trackerId].gyro_buffer;
}

double* NetworkedDeviceQuatServer::getAccel(int trackerId)
{
	if (trackerId < 0 || trackerId >= MAX_TRACKERS) return nullptr;
	return trackers[trackerId].accel_buffer;
}

#define HELLOMESSAGE (" Hey OVR =D 5")

NetworkedDeviceQuatServer::NetworkedDeviceQuatServer()
{
	buff_hello = static_cast<char*>(malloc(sizeof(HELLOMESSAGE)));

	const auto msg = HELLOMESSAGE;
	for (int i = 0; i < sizeof(HELLOMESSAGE); i++)
	{
		buff_hello[i] = msg[i];
	}
	buff_hello[0] = MSG_HANDSHAKE;

	buff_hello_len = sizeof(HELLOMESSAGE);

	// Initialize all tracker data
	for (int i = 0; i < MAX_TRACKERS; i++)
	{
		trackers[i] = TrackerData();
	}
}
