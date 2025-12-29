#pragma once

#include "DeviceQuatServer.h"
#include <map>

#define MSG_HEARTBEAT 0
#define MSG_ROTATION 1
#define MSG_GYRO 2
#define MSG_HANDSHAKE 3
#define MSG_ACCELEROMETER 4

using message_header_type_t = unsigned int;
using message_id_t = unsigned long long;
using sensor_data_t = float;

// message type + packet id
#define MSG_HEADER_SIZE (sizeof(message_header_type_t) + sizeof(message_id_t))

// SlimeVR extensions add some more, just stick with 256 for now
#define MAX_MSG_SIZE 256

/*
first 4 bytes - message type
( 0 = heartbeat
  1 = rotation
  2 = gyro
  3 = handshake
  4 = accelerometer)

next 8 bytes - packet id (+1 every time)

rotation:
(floats)
next 4 bytes - rotation quat x
next 4 bytes - rotation quat y
next 4 bytes - rotation quat z
next 4 bytes - rotation quat w

gyro:
(floats)
next 4 bytes - gyro rate x
next 4 bytes - gyro rate y
next 4 bytes - gyro rate z


64 byte packets
*/

template <typename T>
T convert_chars(unsigned char* src)
{
	union
	{
		unsigned char c[sizeof(T)];
		T v;
	} un;
	for (int i = 0; i < sizeof(T); i++)
	{
		un.c[i] = src[sizeof(T) - i - 1];
	}
	return un.v;
}

// Per-tracker data storage
struct TrackerData
{
	message_id_t current_packet_id = 0;
	double quat_buffer[4] = {0.0, 0.0, 0.0, 1.0}; // identity quaternion (x, y, z, w)
	double gyro_buffer[3] = {0.0, 0.0, 0.0};
	double accel_buffer[3] = {0.0, 0.0, 0.0};
	bool isNewDataAvailable = false;
	unsigned long long lastContactTime = 0;
	bool isConnected = false;

	bool receive_packet_id(message_id_t new_id)
	{
		if ((new_id > current_packet_id) || (new_id < 5))
		{
			current_packet_id = new_id;
			return true;
		}
		return false;
	}
};

class NetworkedDeviceQuatServer : public DeviceQuatServer
{
private:
	void handle_doubles_packet(unsigned char* packet, double* into, int num_doubles, TrackerData& tracker);

protected:
	void handle_gyro_packet(unsigned char* packet, int trackerId);
	void handle_accel_packet(unsigned char* packet, int trackerId);
	void handle_rotation_packet(unsigned char* packet, int trackerId);

	char* buff_hello;
	int buff_hello_len;

	// Per-tracker data storage (up to MAX_TRACKERS)
	TrackerData trackers[MAX_TRACKERS];
	int activeTrackerCount = 0;

public:
	NetworkedDeviceQuatServer();

	// Multi-tracker interface
	int getActiveTrackerCount() override;
	bool isTrackerConnected(int trackerId) override;

	bool isDataAvailable(int trackerId) override;
	double* getRotationQuaternion(int trackerId) override;
	double* getGyroscope(int trackerId) override;
	double* getAccel(int trackerId) override;
};

#define HEARTBEAT_THRESHOLD 1000
#define DEAD_THRESHOLD HEARTBEAT_THRESHOLD*10
