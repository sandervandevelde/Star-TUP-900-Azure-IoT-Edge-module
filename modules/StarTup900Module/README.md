# IoT Edge Module

## container create options

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

