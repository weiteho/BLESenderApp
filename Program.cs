using System;
using System.Collections.Generic;
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
        private RadioButton randomRadioButton;
        private RadioButton customRadioButton;
        private TextBox inputTextBox;
        private Button sendButton;
        private System.Windows.Forms.Timer timer;
        private GattCharacteristic writeCharacteristic;
        private Dictionary<string, ulong> discoveredDevices = new();
        private BluetoothLEAdvertisementWatcher watcher;

        // UUID of the write characteristic (example: Nordic UART TX)
        private readonly Guid writeCharacteristicUuid = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");

        public MainForm()
        {
            Text = "BLE Text Sender";
            Width = 500;
            Height = 400;

            scanButton = new Button { Text = "Scan BLE", Top = 20, Left = 20, Width = 100 };
            scanButton.Click += ScanButton_Click;
            Controls.Add(scanButton);

            deviceComboBox = new ComboBox { Top = 20, Left = 140, Width = 300 };
            Controls.Add(deviceComboBox);

            connectButton = new Button { Text = "Connect", Top = 60, Left = 20, Width = 100 };
            connectButton.Click += ConnectButton_Click;
            Controls.Add(connectButton);

            statusLabel = new Label { Text = "Status: Not connected", Top = 100, Left = 20, Width = 450 };
            Controls.Add(statusLabel);

            randomRadioButton = new RadioButton { Text = "Send Random Text", Top = 140, Left = 20, Checked = true };
            randomRadioButton.CheckedChanged += ModeChanged;
            Controls.Add(randomRadioButton);

            customRadioButton = new RadioButton { Text = "Send Custom Text", Top = 140, Left = 200 };
            customRadioButton.CheckedChanged += ModeChanged;
            Controls.Add(customRadioButton);

            inputTextBox = new TextBox
            {
                Top = 170,
                Left = 20,
                Width = 420,
                Height = 100,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            Controls.Add(inputTextBox);

            sendButton = new Button { Text = "Send", Top = 280, Left = 20, Width = 100 };
            sendButton.Click += SendButton_Click;
            Controls.Add(sendButton);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += Timer_Tick;

            watcher = new BluetoothLEAdvertisementWatcher();
            watcher.Received += Watcher_Received;
            watcher.ScanningMode = BluetoothLEScanningMode.Active;

            ModeChanged(null, null); // Apply initial mode
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            bool isRandom = randomRadioButton.Checked;
            inputTextBox.Enabled = !isRandom;
            sendButton.Enabled = !isRandom;

            if (isRandom)
                timer.Start();
            else
                timer.Stop();
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
                    if (ch.Uuid == writeCharacteristicUuid &&
                        (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                        || ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)))
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
                ModeChanged(null, null); // Refresh timer/UI based on selected mode
            }
            else
            {
                statusLabel.Text = "Write characteristic not found.";
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (writeCharacteristic == null || !randomRadioButton.Checked) return;

            string text = Guid.NewGuid().ToString().Substring(0, 8);
            var writer = new DataWriter();
            writer.WriteString(text);
            //await writeCharacteristic.WriteValueAsync(writer.DetachBuffer());
            await writeCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            statusLabel.Text = $"Sent: {text}";
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            if (writeCharacteristic == null)
            {
                statusLabel.Text = "Not connected.";
                return;
            }

            string text = inputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                statusLabel.Text = "No text to send.";
                return;
            }

            var writer = new DataWriter();
            writer.WriteString(text);
            //await writeCharacteristic.WriteValueAsync(writer.DetachBuffer());
            await writeCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            statusLabel.Text = $"Sent: {text.Replace("\n", " ").Replace("\r", "")}";
        }
    }
}
