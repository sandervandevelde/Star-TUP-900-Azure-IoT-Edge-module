using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using System.Text;
using System.Text.Json;
using System;
using System.IO;
using Microsoft.Azure.Devices.Shared;

namespace StarTup900Module;

internal class ModuleBackgroundService : BackgroundService
{
    const string DefaultPrinterPath = "/dev/usb/lp1";

    private Tup900Commands _tup900Commands;     

    private string _moduleId;
    private string _deviceId;
    private ModuleClient? _moduleClient;
    private CancellationToken _cancellationToken;
    private readonly ILogger<ModuleBackgroundService> _logger;

    public ModuleBackgroundService(ILogger<ModuleBackgroundService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {

        _logger.LogInformation(@"                _     _             _       _                  _                       _                    _              ___   ___   ___  ");
        _logger.LogInformation(@"               | |   | |           (_)     | |                | |                     | |                  | |            / _ \ / _ \ / _ \ ");
        _logger.LogInformation(@"  _____   _____| | __| | ___ ______ _  ___ | |_ ______ ___  __| | __ _  ___ ______ ___| |_ __ _ _ __ ______| |_ _   _ _ _| (_) | | | | | | |");
        _logger.LogInformation(@" / __\ \ / / _ \ |/ _` |/ _ \______| |/ _ \| __|______/ _ \/ _` |/ _` |/ _ \______/ __| __/ _` | '__|______| __| | | | '_ \__, | | | | | | |");
        _logger.LogInformation(@" \__ \\ V /  __/ | (_| |  __/      | | (_) | |_      |  __/ (_| | (_| |  __/      \__ \ || (_| | |         | |_| |_| | |_) |/ /| |_| | |_| |");
        _logger.LogInformation(@" |___/ \_/ \___|_|\__,_|\___|      |_|\___/ \__|      \___|\__,_|\__, |\___|      |___/\__\__,_|_|          \__|\__,_| .__//_/  \___/ \___/ ");
        _logger.LogInformation(@"                                                                  __/ |                                              | |                    ");
        _logger.LogInformation(@"                                                                 |___/                                               |_|                    ");

        _tup900Commands= new Tup900Commands{ PrinterPath = DefaultPrinterPath };

        _cancellationToken = cancellationToken;
        MqttTransportSettings mqttSetting = new(TransportType.Mqtt_Tcp_Only);
        ITransportSettings[] settings = { mqttSetting };

       _deviceId = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
       _moduleId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");

        // Open a connection to the Edge runtime
        _moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);

        // Reconnect is not implemented because we'll let docker restart the process when the connection is lost
        _moduleClient.SetConnectionStatusChangesHandler((status, reason) => 
            _logger.LogWarning("Connection changed: Status: {status} Reason: {reason}", status, reason));

        _moduleClient.SetDesiredPropertyUpdateCallbackAsync((desiredProperties, userContext) =>
            {
                _logger.LogInformation("Desired properties update received");

                LoadDeviceTwin().Wait();

                return Task.CompletedTask;
            }, null, cancellationToken).Wait();

        _logger.LogInformation($"Desired properties set (accepts property 'printerPath').");

        await _moduleClient.SetMethodHandlerAsync(
            "print",
            printMethodCallBack,
            _moduleClient);

        _logger.LogInformation($"Method 'print' set (accepts JSON with 'name').");

        await _moduleClient.SetMethodHandlerAsync(
            "status",
            statusMethodCallBack,
            _moduleClient);

        _logger.LogInformation($"Method 'status' set.");

        await _moduleClient.OpenAsync(cancellationToken);

        _logger.LogInformation($"Module '{_deviceId}'-'{_moduleId}' initialized.");

        await LoadDeviceTwin();
    }

    private async Task LoadDeviceTwin()
    {
        _logger.LogInformation($"---------------------" );

        try
        {
            var twin = await _moduleClient.GetTwinAsync(_cancellationToken);

            _logger.LogInformation($"Device Twin for module '{_moduleId}' retrieved with desired properties: {twin.Properties.Desired.ToJson()}");

            var reportedProperties = new TwinCollection();

            if (twin.Properties.Desired.Contains("printerPath")) 
            {
                if (twin.Properties.Desired["printerPath"] != null)
                {
                    _tup900Commands.PrinterPath = twin.Properties.Desired["printerPath"];
                }
                else
                {
                    _tup900Commands.PrinterPath = DefaultPrinterPath;
                }

                Console.WriteLine($"printerPath changed to {_tup900Commands.PrinterPath}");

                reportedProperties["printerPath"] = _tup900Commands.PrinterPath;
            }

            if (reportedProperties.Count > 0)
            {
                await _moduleClient.UpdateReportedPropertiesAsync(reportedProperties);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception '{ex.Message}' while retrieving device twin.");
        }
    }

    private async Task<MethodResponse> statusMethodCallBack(MethodRequest methodRequest, object userContext)
    {  
        _logger.LogInformation($"=====================" );

        var statusResponse = new StatusResponse
        {
            deviceId = _deviceId,
            timestamp = DateTime.UtcNow, 
            status = "Status method called and status read." 
        };

        // Create a message to send to the output queue when the message is printed

        var responseCode = 200;

        try
        {
            using (FileStream fs = new FileStream(_tup900Commands.PrinterPath, FileMode.Open, FileAccess.ReadWrite))
            {
                // 1. Enable Asb status monitoring
                fs.Write(_tup900Commands.EnableAsbStatusCommand, 0, _tup900Commands.EnableAsbStatusCommand.Length);
                fs.Flush();

                // 2. Read back the status byte
                // Note: You may need a small delay or a loop to wait for the printer to reply

                byte[] asbBuffer = new byte[10];
                int bytesRead = fs.Read(asbBuffer, 0, asbBuffer.Length);

                if (bytesRead > 0) 
                {
                    _logger.LogInformation($"Asb status list: {Convert.ToHexString(asbBuffer)}" );

                    //// Analyze bits in the status byte (refer to Star Line Mode Manual)

                    // Presenter Paper, Ninth byte
                    var paperTaken = ((asbBuffer[8] & 0x04) == 0x00) && ((asbBuffer[8] & 0x02) == 0x00);
                    _logger.LogInformation(paperTaken ? "Paper Taken or Collected" : "Paper still in slot");
                    statusResponse.paperCollected = paperTaken;

                    // Paper role missing, sixth byte
                    var rollMissing = (asbBuffer[5] & 0x04) == 0x04;
                    _logger.LogInformation(rollMissing ? "Roll missing" : "Roll placed");
                    statusResponse.rollMissing = rollMissing;
                }
                else
                {
                    _logger.LogInformation("No response from status request");
                }
            }                    
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception '{ex.Message}' while processing status method call.");
            responseCode = 500;
            statusResponse.status = $"Failed to read status ({ex.Message})";
        }

        // Send to the output queue when the status is retrieved or not

        await SendStatusMessage(statusResponse);

        // Send status method response message

        var json = JsonSerializer.Serialize(statusResponse);
        var response = new MethodResponse(Encoding.UTF8.GetBytes(json), responseCode);

        _logger.LogInformation($"Status method response '{json}' returned at {DateTime.UtcNow}.");

        return response;
    }

    private async Task<MethodResponse> printMethodCallBack(MethodRequest methodRequest, object userContext)
    {       
        _logger.LogInformation($"+++++++++++++++++++++" );

        var printResponse = new PrintResponse
        {
            deviceId = _deviceId,
            timestamp = DateTime.UtcNow, 
            status = "Message deserialized and printed." 
        };
        
        // print the message received from the method call

        var responseCode = 200;

        try
        {
            var messageDate = Encoding.UTF8.GetString(methodRequest.Data);

            var printMethodRequest = JsonSerializer.Deserialize<PrintMethodRequest>(messageDate);

            _logger.LogInformation($"Print method called with message '{messageDate}' at {DateTime.UtcNow}.");

            //// Print

            string toPrint1 = $"Hello {printMethodRequest.name}, welcome to this Azure IoT Edge C# module running on Ubuntu!";
            byte[] toPrint1Buffer = System.Text.Encoding.ASCII.GetBytes(toPrint1);

            using (FileStream fs = new FileStream(_tup900Commands.PrinterPath, FileMode.Open, FileAccess.Write))
            {
                fs.Write(_tup900Commands.Dot12PitchCommand, 0, _tup900Commands.Dot12PitchCommand.Length);
                fs.Write(toPrint1Buffer, 0, toPrint1Buffer.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.StartEmphasizedCommand, 0, _tup900Commands.StartEmphasizedCommand.Length);
                fs.Write(_tup900Commands.Dot15PitchCommand, 0, _tup900Commands.Dot15PitchCommand.Length);
                fs.Write(toPrint1Buffer, 0, toPrint1Buffer.Length);
                fs.Write(_tup900Commands.CancelEmphasizedCommand, 0, _tup900Commands.CancelEmphasizedCommand.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.StartUnderlineCommand, 0, _tup900Commands.StartUnderlineCommand.Length);
                fs.Write(_tup900Commands.Dot16PitchCommand, 0, _tup900Commands.Dot16PitchCommand.Length);
                fs.Write(toPrint1Buffer, 0, toPrint1Buffer.Length);
                fs.Write(_tup900Commands.CancelUnderlineCommand, 0, _tup900Commands.CancelUnderlineCommand.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.StartInverseCommand, 0, _tup900Commands.StartInverseCommand.Length);
                fs.Write(toPrint1Buffer, 0, toPrint1Buffer.Length);
                fs.Write(_tup900Commands.CancelInverseCommand, 0, _tup900Commands.CancelInverseCommand.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.PrintLogoCommand, 0, _tup900Commands.PrintLogoCommand.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.PrintCode128Command, 0, _tup900Commands.PrintCode128Command.Length);

                fs.Write(_tup900Commands.LfCommand, 0, _tup900Commands.LfCommand.Length);

                fs.Write(_tup900Commands.CutCommand, 0, _tup900Commands.CutCommand.Length);

                fs.Write(_tup900Commands.SetRecoveryTimespanCommand, 0, _tup900Commands.SetRecoveryTimespanCommand.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception '{ex.Message}' while processing print method call.");
            responseCode = 500;
            printResponse.status = $"Failed to print message ({ex.Message})";
        }

        // Send to the output queue when the message is printed or not

        await SendPrintMessage(printResponse);

        // Send print method response message

        var json = JsonSerializer.Serialize(printResponse);
        var response = new MethodResponse(Encoding.UTF8.GetBytes(json), responseCode);

        _logger.LogInformation($"Print method response '{json}' returned at {DateTime.UtcNow}.");

        return response;
    }

    private async Task SendPrintMessage(PrintResponse printMessage)
    {
        var jsonMessage = JsonSerializer.Serialize(printMessage);

        using (var message = new Message(Encoding.UTF8.GetBytes(jsonMessage)))
        { 
            // Set message body type and content encoding for routing using decoded body values.
            message.ContentEncoding = "utf-8";
            message.ContentType = "application/json";

            try
            {
                await _moduleClient.SendEventAsync("output1", message);

                _logger.LogInformation($"Print event message '{jsonMessage}' sent at {DateTime.UtcNow}.");

            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Exception '{ex.Message}' while sending output message '{jsonMessage}'");
            }
        }
    }    

    private async Task SendStatusMessage(StatusResponse statusMessage)
    {
        _logger.LogInformation($"~~~~~~~~~~~~~~~~~~~~~" );

        var jsonMessage = JsonSerializer.Serialize(statusMessage);

        using (var message = new Message(Encoding.UTF8.GetBytes(jsonMessage)))
        { 
            // Set message body type and content encoding for routing using decoded body values.
            message.ContentEncoding = "utf-8";
            message.ContentType = "application/json";

            try
            {
                await _moduleClient.SendEventAsync("output1", message);

                _logger.LogInformation($"Status event message '{jsonMessage}' sent at {DateTime.UtcNow}.");

            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Exception '{ex.Message}' while sending output message '{jsonMessage}'");
            }
        }
    }    

}

internal class PrintMethodRequest
{
    public required string name { get; set; }
}

internal class PrintResponse
{
    public required string deviceId { get; set; }
    public DateTime timestamp { get; set; }
    public required string status { get; set; }
}

internal class StatusResponse
{
    public required string deviceId { get; set; }
    public DateTime timestamp { get; set; }
    public required string status { get; set; }
    public bool paperCollected { get; set; }
    public bool rollMissing { get; set; }
}

internal class Tup900Commands
{
    // Use the path identified as printer device
    // In production, Status and Print method execution should be decoupled to avoid conflicts when trying to print and read status at the same time. 
    // This sample is simplified for demo purposes by using the same path and allowing the possibility of conflicts.
    public required string PrinterPath { get; set; }

    // Line Feed command (including Carriage Return)
    public byte[] LfCommand {get;set;} = new byte[] { 0x0a };

    // Pitch (white space between characters)
    public byte[] Dot12PitchCommand {get;set;} = new byte[] { 0x1b, 0x4d };
    public byte[] Dot15PitchCommand {get;set;} = new byte[] { 0x1b, 0x50 };
    public byte[] Dot16PitchCommand {get;set;} = new byte[] { 0x1b, 0x3a };

    // Select and Cancel emphasized text
    public byte[] StartEmphasizedCommand {get;set;} = new byte[] { 0x1b, 0x45 };
    public byte[] CancelEmphasizedCommand {get;set;} = new byte[] { 0x1b, 0x46 };

    // Select and Cancel Underline text
    public byte[] StartUnderlineCommand {get;set;} = new byte[] { 0x1b, 0x2d, 0x01 };
    public byte[] CancelUnderlineCommand {get;set;} = new byte[] { 0x1b, 0x2d, 0x00 };

    // Select and Cancel Inverse white/black text
    public byte[] StartInverseCommand {get;set;} = new byte[] { 0x1b, 0x34 };
    public byte[] CancelInverseCommand {get;set;} = new byte[] { 0x1b, 0x35 };

    // 'Cut' command (Hex: 0x1b 0x64 0x02 for Star Line Mode)
    public byte[] CutCommand {get;set;} = new byte[] { 0x1b, 0x64, 0x02 };

    // Print preloaded Logo
    public byte[] PrintLogoCommand {get;set;} = new byte[] { 0x1b, 0x1c, 0x70, 0x01, 0x00 };

    // Print 'MVP' as Code128 barcode -> 1B 62 n1=06=Code128 n2=02=underbar n3=02=modeselect n2=A0=dotcount, D1=4D=M, D2=56=V, D3=50=P, 1E
    public byte[] PrintCode128Command {get;set;} = new byte[] { 0x1b, 0x62, 0x06, 0x02, 0x02, 0xA0, 0x4d , 0x56, 0x50, 0x1e };

    // Set presenter paper automatic recovery function and automatic recovery time (64/2 = 32 seconds in this example) (ESC RS 1 n m - Set presenter paper automatic recovery function and automatic recovery time)
    public byte[] SetRecoveryTimespanCommand {get;set;} = new byte[] { 0x1b, 0x16, 0x31, 0x40 };

    // Automatic recovery by presenter = Direct Execution
    public byte[] ExecuteRecoveryCommand {get;set;} = new byte[] { 0x1b, 0x16, 0x30, 0x0 };

    // Enable Presenter Status (ESC RS a n - Set status transmission conditions)
    public byte[] EnableAsbStatusCommand {get;set;} = new byte[] { 0x1B, 0x1E, 0x61, 0x04 };
}