using UnityEngine;

public readonly struct LongitudinalControlInput
{
    public readonly float CurrentSpeedUnitsPerSecond;
    public readonly float DesiredSpeedUnitsPerSecond;
    public readonly float DeltaSeconds;
    public readonly float PreviousAccelerationUnitsPerSecondSquared;
    public readonly float AccelerationUnitsPerSecondSquared;
    public readonly float ServiceDecelerationUnitsPerSecondSquared;
    public readonly float EmergencyDecelerationUnitsPerSecondSquared;
    public readonly float MaximumJerkUnitsPerSecondCubed;

    public LongitudinalControlInput(
        float currentSpeedUnitsPerSecond,
        float desiredSpeedUnitsPerSecond,
        float deltaSeconds,
        float previousAccelerationUnitsPerSecondSquared,
        float accelerationUnitsPerSecondSquared,
        float serviceDecelerationUnitsPerSecondSquared,
        float emergencyDecelerationUnitsPerSecondSquared,
        float maximumJerkUnitsPerSecondCubed)
    {
        CurrentSpeedUnitsPerSecond = currentSpeedUnitsPerSecond;
        DesiredSpeedUnitsPerSecond = desiredSpeedUnitsPerSecond;
        DeltaSeconds = deltaSeconds;
        PreviousAccelerationUnitsPerSecondSquared =
            previousAccelerationUnitsPerSecondSquared;
        AccelerationUnitsPerSecondSquared = accelerationUnitsPerSecondSquared;
        ServiceDecelerationUnitsPerSecondSquared =
            serviceDecelerationUnitsPerSecondSquared;
        EmergencyDecelerationUnitsPerSecondSquared =
            emergencyDecelerationUnitsPerSecondSquared;
        MaximumJerkUnitsPerSecondCubed = maximumJerkUnitsPerSecondCubed;
    }
}

public readonly struct LongitudinalControlResult
{
    public readonly float SpeedUnitsPerSecond;
    public readonly float AccelerationUnitsPerSecondSquared;
    public readonly bool EmergencyClampApplied;

    public LongitudinalControlResult(
        float speedUnitsPerSecond,
        float accelerationUnitsPerSecondSquared,
        bool emergencyClampApplied)
    {
        SpeedUnitsPerSecond = speedUnitsPerSecond;
        AccelerationUnitsPerSecondSquared =
            accelerationUnitsPerSecondSquared;
        EmergencyClampApplied = emergencyClampApplied;
    }
}

public static class LongitudinalController
{
    public static LongitudinalControlResult Step(
        LongitudinalControlInput input)
    {
        float deltaSeconds = Mathf.Max(0.0001f, input.DeltaSeconds);
        float targetAcceleration =
            (input.DesiredSpeedUnitsPerSecond -
             input.CurrentSpeedUnitsPerSecond) /
            deltaSeconds;
        float clampedAcceleration = Mathf.Clamp(
            targetAcceleration,
            -Mathf.Max(0.1f, input.ServiceDecelerationUnitsPerSecondSquared),
            Mathf.Max(0.1f, input.AccelerationUnitsPerSecondSquared));

        float maxAccelerationDelta =
            Mathf.Max(0.1f, input.MaximumJerkUnitsPerSecondCubed) *
            deltaSeconds;
        clampedAcceleration = Mathf.MoveTowards(
            input.PreviousAccelerationUnitsPerSecondSquared,
            clampedAcceleration,
            maxAccelerationDelta);

        bool emergencyClamp = false;
        float emergencyDecel =
            Mathf.Max(
                input.ServiceDecelerationUnitsPerSecondSquared,
                input.EmergencyDecelerationUnitsPerSecondSquared);
        if (clampedAcceleration < -emergencyDecel)
        {
            clampedAcceleration = -emergencyDecel;
            emergencyClamp = true;
        }

        float speed = Mathf.Max(
            0f,
            input.CurrentSpeedUnitsPerSecond +
            clampedAcceleration * deltaSeconds);
        if (input.DesiredSpeedUnitsPerSecond <= 0f &&
            speed < 0.02f)
        {
            speed = 0f;
        }

        return new LongitudinalControlResult(
            speed,
            clampedAcceleration,
            emergencyClamp);
    }
}
