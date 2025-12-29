#pragma once
#include "TrackingHandler.g.h"

#include <chrono>
#include <fstream>
#include <thread>
#include <WinSock2.h>
#include <iphlpapi.h>
#include <WS2tcpip.h>
#include <array>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "iphlpapi.lib")

#include <future>
#include <InfoServer.h>
#include <PositionPredictor.h>
#include <UDPDeviceQuatServer.h>

/* Status enumeration */
#define R_E_CON_DEAD 0x00010001    // No connection
#define R_E_NO_DATA 0x00010002     // No data received
#define R_E_INIT_FAILED 0x00010003 // Init failed
#define R_E_PORTS_TAKEN 0x00010004 // Ports taken
#define R_E_NOT_STARTED 0x00010005 // Disconnected (initial)

namespace winrt::DeviceHandler::implementation
{
    // Per-tracker calibration state
    struct PerTrackerState
    {
        Quaternion globalRotation{0, 0, 0, 1};
        Quaternion localRotation{0, 0, 0, 1};
        bool calibratingForward = false;
        bool calibratingDown = false;
    };

    struct TrackingHandler : TrackingHandlerT<TrackingHandler>
    {
        TrackingHandler() = default;

        void OnLoad();
        void Update();

        int32_t Initialize();
        int32_t Shutdown();

        int32_t Port() const;
        void Port(int32_t value);

        com_array<hstring> IP() const;
        bool IsInitialized() const;
        int32_t StatusResult() const;

        // Multi-tracker support
        int32_t TrackerCount() const;
        com_array<TrackerInfo> GetTrackerInfos() const;

        // Per-tracker calibration
        Quaternion GetGlobalRotation(int32_t trackerId) const;
        void SetGlobalRotation(int32_t trackerId, const Quaternion& value);
        Quaternion GetLocalRotation(int32_t trackerId) const;
        void SetLocalRotation(int32_t trackerId, const Quaternion& value);

        bool GetCalibratingForward(int32_t trackerId) const;
        void SetCalibratingForward(int32_t trackerId, bool value);
        bool GetCalibratingDown(int32_t trackerId) const;
        void SetCalibratingDown(int32_t trackerId, bool value);

        // Legacy single-device calibration (uses tracker 0)
        bool CalibratingForward() const;
        void CalibratingForward(bool value);
        bool CalibratingDown() const;
        void CalibratingDown(bool value);
        Quaternion GlobalRotation() const;
        void GlobalRotation(const Quaternion& value);
        Quaternion LocalRotation() const;
        void LocalRotation(const Quaternion& value);

        event_token StatusChanged(const Windows::Foundation::EventHandler<hstring>& handler);
        void StatusChanged(const event_token& token) noexcept;

        event_token LogEvent(const Windows::Foundation::EventHandler<hstring>& handler);
        void LogEvent(const event_token& token) noexcept;

        // Per-tracker pose calculation
        Pose CalculatePoseForTracker(
            int32_t trackerId,
            const Pose& headsetPose,
            const float& headsetYaw,
            const Vector& globalOffset,
            const Vector& deviceOffset,
            const Vector& trackerOffset);

        // Legacy single-device pose (uses tracker 0)
        Pose CalculatePose(
            const Pose& headsetPose,
            const float& headsetYaw,
            const Vector& globalOffset,
            const Vector& deviceOffset,
            const Vector& trackerOffset);

        // Per-tracker haptics
        void SignalTracker(int32_t trackerId) const;

        // Legacy single-device signal (uses tracker 0)
        void Signal() const;

    private:
        event<Windows::Foundation::EventHandler<hstring>> statusChangedEvent;
        event<Windows::Foundation::EventHandler<hstring>> logEvent;

        std::function<void(std::wstring, int32_t)> Log = std::bind(
            &TrackingHandler::LogMessage, this, std::placeholders::_1, std::placeholders::_2);

        std::shared_ptr<std::future<void>> reloadThread;

        bool initialized = false;

        uint32_t devicePort = 6969;

        std::vector<hstring> ipVector;
        HRESULT statusResult = R_E_NOT_STARTED;

        UDPDeviceQuatServer* dataServer;
        InfoServer* infoServer;
        PositionPredictor posePredictor;

        // Per-tracker calibration states (up to MAX_TRACKERS)
        std::array<PerTrackerState, MAX_TRACKERS> trackerStates;

        // How many retries have been made before marking
        // the connection dead (assume max 180 retries or 3 seconds)
        int32_t eRetries = 0;

        // Delayed sending of refresh requests
        void CallStatusChanged(const std::wstring& message, HRESULT status);

        // Message logging handler: bound to <Log>
        void LogMessage(const std::wstring& message, const int32_t& severity)
        {
            logEvent(*this, std::format(L"[{}] ", severity) + message);
        }
    };
}

namespace winrt::DeviceHandler::factory_implementation
{
    struct TrackingHandler : TrackingHandlerT<TrackingHandler, implementation::TrackingHandler>
    {
    };
}
