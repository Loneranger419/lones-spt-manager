namespace Lones.SptManager.Core.Deploy;

public static class OverlayFailure
{
    public static string Explain(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("empty or already a manager junction", StringComparison.OrdinalIgnoreCase))
        {
            return message
                   + " Import the leftover into the store (or delete it) so the overlay path is empty. Junctions need an empty directory or an existing manager link.";
        }

        if (message.Contains("privilege is missing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("privilege not held", StringComparison.OrdinalIgnoreCase))
        {
            return message
                   + " Check folder ACL: you need permission to create a directory on the install overlay path. Elevation is not required for a normal junction.";
        }

        if (message.Contains("source directory must be empty", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Junction path must not already exist", StringComparison.OrdinalIgnoreCase))
        {
            return "Junction failed because the install path was not empty. "
                   + message
                   + " Empty that folder, import it as a leftover, or remove the leftover files first.";
        }

        return message;
    }
}
