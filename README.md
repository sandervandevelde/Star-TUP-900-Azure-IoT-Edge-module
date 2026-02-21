# Star TUP 900 Azure IoT Edge module

Azure IoT Edge module demonstration for a STAR TUP 900 kiosk thermal printer.

NOTE: THIS MODULE IS A DEMONSTRATION. DO NOT USE THIS FOR PRODUCTION!

## Deployment

The module as-is can be taken from my Docker hub: 'svelde/iot-edge-star-tup900'.

This version is intended for Ubuntu on an Intel processor.

If you want to deploy this module on your own container repository, Fix the module.json file.

Build and deployment of the module can be done via the Azure IoT Edge extension for Visual Studio Code.

## What does it do?

### Desired and reported property

The module takes one desired property:

```
{
  "printerPath" : "/dev/usb/lp1"
}
```

### Direct method: PRint

You can print a demonstration text via: 'print'.

This takes one parameter via JSON:

```
{
    "name": "Loes"
}
```

If the message is received and the print is printed, the answer is:

```
{
    "status": 200,
    "payload": {
        "deviceId": "uno2372g01advantech",
        "timestamp": "2026-02-21T22:32:48.4306328Z",
        "status": "Message deserialized and printed."
    }
}
```

A 500 Status code is returned with the error is an exception occurs. 

This 500 status is seen when no access to the printer port was available.

### Direct method: Status

You can ask for the current status via 'status'.

This call takes no parameters.

The answer ,when accepted, will be:

```
{
    "status": 200,
    "payload": {
        "deviceId": "uno2372g01advantech",
        "timestamp": "2026-02-21T22:49:32.933633Z",
        "status": "Status method called and status read.",
        "paperCollected": true,
        "rollMissing": false
    }
}
```

this means both the availability of paper in the presenter and a printer roll is tested.

Again, a 500 status is returned when there is an exception.

## Telemetry messages

Calling the print and status direct methods will also invoke sending telemetry messages:

The status message looks like: 

```
{
  "deviceId":"uno2372g01advantech",
  "timestamp":"2026-02-21T22:52:14.0629631Z",
  "status":"Status method called and status read.",
  "paperCollected":true,
  "rollMissing":false
}
```

The print message looks like:

```
{
  "deviceId":"uno2372g01advantech",
  "timestamp":"2026-02-21T22:52:21.5847152Z",
  "status":"Message deserialized and printed."
}
```

When an error occurs, this is shown in the message too.

## Container create options

The Star TUP 900 printer tested has a USB cable.

Within Linux Ubuntu, it is accessible at port '/dev/usb/lp1'.

The container create options needed in this example are:

```
{
  "HostConfig": {
    "Devices": [
      {
        "PathOnHost": "/dev/usb/lp1",
        "PathInContainer": "/dev/usb/lp1",
        "CgroupPermissions": "mrw"
      }
    ]
  }
}
```

You can test this via:

```
sudo docker exec tup ls -l /dev/u*
```

### Elevated rights

Notice that the port needs to provide elevated rights so an Azure IoT Edge module can access it.

These elevated rights are removed when the OS is rebooted, when the printer is shut down and restarted again, or when the printer USB is removed and attached.

This will affect the access to the port for the container.

Worst case, the container must be restarted to pick up the right access rights again.

Here, the access rights are elevated after booting the machine.

I created this '/etc/rc.local' file with this content:

```
#!/bin/bash
sudo chmod a+rw /dev/usb/lp1
exit 0
```

The file will be executed while booting via:

```
sudo chmod 777 /etc/rc.local
```

*Note*: This has to be done only once.

## MIT License

This file is available under MIT license. Check that file in the repo to understand how you can use it.

If you think you can contribute to this demonstration, pull requests are accepted.