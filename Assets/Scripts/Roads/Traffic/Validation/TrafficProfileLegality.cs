public static class TrafficProfileLegality
{
    public static bool ValidateVehicleCanUseRoad(
        RoadProfile roadProfile,
        VehicleTrafficProfile vehicleProfile,
        TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null) diagnostics = new TrafficDiagnosticCollection();
        if (roadProfile == null || vehicleProfile == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.IllegalProfileCombination,
                "Cannot validate traffic legality with a missing road or vehicle profile.");
            return false;
        }

        int initialCount = diagnostics.Count;
        TrafficDiagnosticSource source = TrafficDiagnosticSource.ForProfile(
            roadProfile.SourceKey,
            roadProfile.Id.ToString());

        if ((roadProfile.AllowedPermissions & vehicleProfile.RoadPermissions) == 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.IllegalProfileCombination,
                "Vehicle road permissions do not overlap this road profile.",
                source);
        }

        if ((roadProfile.AllowedCapabilities & vehicleProfile.Capabilities) == 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.IllegalProfileCombination,
                "Vehicle capabilities do not overlap this road profile.",
                source);
        }

        for (int i = initialCount; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == TrafficDiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }

    public static bool ValidateRoadSupportsMovement(
        RoadProfile roadProfile,
        RoadMovementPolicyMask requiredMovement,
        TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null) diagnostics = new TrafficDiagnosticCollection();
        if (roadProfile == null ||
            requiredMovement == RoadMovementPolicyMask.None ||
            (roadProfile.SupportedMovements & requiredMovement) == 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.IllegalProfileCombination,
                "Road profile does not support the required traffic movement.");
            return false;
        }

        return true;
    }
}
