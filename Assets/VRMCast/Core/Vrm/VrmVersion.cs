namespace VRMCast.Core.Vrm
{
    /// <summary>
    /// VRM specification family of a file. Detected from the glTF extension keys in the file header.
    /// </summary>
    public enum VrmVersion
    {
        /// <summary>The file could not be identified as VRM.</summary>
        Unknown = 0,

        /// <summary>VRM 0.x ("VRM" glTF extension).</summary>
        Vrm0 = 1,

        /// <summary>VRM 1.0 ("VRMC_vrm" glTF extension).</summary>
        Vrm1 = 2,
    }

    /// <summary>
    /// User selection in the advanced import dialog. Auto is the default and the only option
    /// exposed during a normal successful load (PRD 5.1).
    /// </summary>
    public enum VrmVersionOverride
    {
        AutoDetect = 0,
        ForceVrm0 = 1,
        ForceVrm1 = 2,
    }
}
