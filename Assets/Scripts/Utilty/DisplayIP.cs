using UnityEngine;
using TMPro;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Linq;

public class DisplayIP : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI textDisplay;

    private void Start()
    {
        if (textDisplay == null)
        {
            textDisplay = GetComponent<TextMeshProUGUI>();

            if (textDisplay == null)
            {
                Debug.LogError("No TextMeshProUGUI component found! Please assign one in the inspector or attach this script to a GameObject with a TextMeshProUGUI component.");
                return;
            }
        }

        string ipAddress = GetLocalIPAddress();
        textDisplay.text = $"{ipAddress}";
    }

    private string GetLocalIPAddress()
    {
        string ipAddress = "Not Found";

        try
        {
            // Get all network interfaces
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(ni => ni.Speed); // Prioritize faster connections

            foreach (var networkInterface in networkInterfaces)
            {
                // Get IP properties for this interface
                var properties = networkInterface.GetIPProperties();

                // Look for IPv4 addresses
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        // Found an IPv4 address that's not loopback
                        return address.Address.ToString();
                    }
                }
            }

            // Fallback method if the above fails
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                ipAddress = endPoint.Address.ToString();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting IP address: {ex.Message}");
        }

        return ipAddress;
    }
}