using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BLESenderApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private ComboBox deviceComboBox;
        private Button scanButton;
        private Button connectButton;
        private Label statusLabel;
        private System.Windows.Forms.Timer timer;
        private GattCharacteristic writeCharacteristic;
        private Dictionary<string, ulong> discoveredDevices = new();
        private BluetoothLEAdvertisementWatcher watcher;

        // UUID of the write characteristic (example: Nordic UART)
        private readonly Guid writeCharacteristicUuid = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");

        public MainForm()
        {
            Text = "BLE Random Text Sender";
            Width = 450;
            Height = 250;

            scanButton = new Button { Text = "Scan BLE", Top = 20, Left = 20, Width = 100 };
            scanButton.Click += ScanButton_Click;
            Controls.Add(scanButton);

            deviceComboBox = new ComboBox { Top = 20, Left = 140, Width = 250 };
            Controls.Add(deviceComboBox);

            connectButton = new Button { Text = "Connect", Top = 60, Left = 20, Width = 100 };
            connectButton.Click += ConnectButton_Click;
            Controls.Add(connectButton);

            statusLabel = new Label { Text = "Status: Not connected", Top = 100, Left = 20, Width = 400 };
            Controls.Add(statusLabel);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += Timer_Tick;

            watcher = new BluetoothLEAdvertisementWatcher();
            watcher.Received += Watcher_Received;
            watcher.ScanningMode = BluetoothLEScanningMode.Active;
        }

        private void ScanButton_Click(object sender, EventArgs e)
        {
            discoveredDevices.Clear();
            deviceComboBox.Items.Clear();
            statusLabel.Text = "Scanning...";
            watcher.Start();

            // Stop scanning after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                watcher.Stop();
                Invoke(() => statusLabel.Text = "Scan complete");
            });
        }

        private void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            string name = args.Advertisement.LocalName;
            if (!string.IsNullOrEmpty(name))
            {
                string display = $"{name} ({args.BluetoothAddress:X})";
                if (!discoveredDevices.ContainsKey(display))
                {
                    discoveredDevices[display] = args.BluetoothAddress;
                    Invoke(() => deviceComboBox.Items.Add(display));
                }
            }
        }

        private async void ConnectButton_Click(object sender, EventArgs e)
        {
            if (deviceComboBox.SelectedItem is null)
            {
                statusLabel.Text = "Select a device first.";
                return;
            }

            string selected = deviceComboBox.SelectedItem.ToString();
            ulong address = discoveredDevices[selected];

            statusLabel.Text = "Connecting...";
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device == null)
            {
                statusLabel.Text = "Device connection failed.";
                return;
            }

            GattDeviceServicesResult servicesResult = await device.GetGattServicesAsync();
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                statusLabel.Text = "Service discovery failed.";
                return;
            }

            foreach (var service in servicesResult.Services)
            {
                var charResult = await service.GetCharacteristicsAsync();
                foreach (var ch in charResult.Characteristics)
                {
                    if (ch.Uuid == writeCharacteristicUuid && ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
                    {
                        writeCharacteristic = ch;
                        break;
                    }
                }
                if (writeCharacteristic != null)
                    break;
            }

            if (writeCharacteristic != null)
            {
                statusLabel.Text = "Connected and ready!";
                timer.Start();
            }
            else
            {
                statusLabel.Text = "Write characteristic not found.";
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (writeCharacteristic == null) return;

            string text = Guid.NewGuid().ToString().Substring(0, 8);
            var writer = new DataWriter();
            writer.WriteString(text);
            await writeCharacteristic.WriteValueAsync(writer.DetachBuffer());
            statusLabel.Text = $"Sent: {text}";
        }
    }
}
