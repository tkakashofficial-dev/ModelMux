namespace ModelMux;

/// <summary>
/// Thrown when ModelMux is misconfigured — an unknown provider, a missing profile, or a
/// credential that isn't where the configuration says it is.
/// </summary>
/// <remarks>
/// Messages are written to name the exact configuration key at fault and what to do about
/// it. A configuration error discovered at 3am should not require reading the source.
/// </remarks>
public sealed class ModelMuxConfigurationException : Exception
{
    /// <summary>Creates the exception with a message describing the misconfiguration.</summary>
    public ModelMuxConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an underlying cause.</summary>
    public ModelMuxConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
