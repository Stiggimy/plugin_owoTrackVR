#pragma once

// abstract class so other implementations can be made
// (bluetooth, etc)

#define MAX_TRACKERS 20

class DeviceQuatServer
{
public:
	virtual void startListening(bool& _ret) = 0; // set up server
	virtual void tick() = 0; // tick

	// Multi-tracker support
	virtual int getActiveTrackerCount() = 0; // number of connected trackers
	virtual bool isTrackerConnected(int trackerId) = 0; // check if specific tracker is connected

	virtual bool isDataAvailable(int trackerId) = 0; // true if new data is available for tracker
	virtual double* getRotationQuaternion(int trackerId) = 0; // rotation quat {x, y, z, w}
	virtual double* getGyroscope(int trackerId) = 0; // gyro rad/s {x, y, z}
	virtual double* getAccel(int trackerId) = 0; // accelerometer m/s^2 {x, y, z}

	virtual bool isConnectionAlive(int trackerId) = 0; // checks if tracker connection is still alive

	virtual void buzz(int trackerId, float duration_s, float frequency, float amplitude) = 0; // vibrates specific tracker

	virtual int get_port() = 0; // returns port or other unique id
};
