use anyhow::Result;
use tracing::{debug, info, warn};
use wasapi::{DeviceCollection, Direction};

use super::AudioOutput;

pub struct UsbAudioOutput {
    device_name: Option<String>,
    buffer: Vec<u8>,
    bytes_written: u64,
}

impl UsbAudioOutput {
    pub fn new(device_name: Option<String>) -> Result<Self> {
        info!("UsbAudioOutput: Creating output for device: {:?}", device_name);

        if let Some(ref name) = device_name {
            if Self::find_device_name(name).is_none() {
                warn!("UsbAudioOutput: Device '{}' not found, will use default", name);
            }
        }

        Ok(Self {
            device_name,
            buffer: Vec::with_capacity(64 * 1024),
            bytes_written: 0,
        })
    }

    fn find_device_name(target: &str) -> Option<String> {
        let collection = DeviceCollection::new(&Direction::Render).ok()?;

        for device in collection.into_iter() {
            if let Ok(dev) = device {
                if let Ok(name) = dev.get_friendlyname() {
                    if name.to_lowercase().contains(&target.to_lowercase()) {
                        return Some(name);
                    }
                }
            }
        }
        None
    }

    pub fn list_devices() -> Result<Vec<String>> {
        let collection = DeviceCollection::new(&Direction::Render)
            .map_err(|e| anyhow::anyhow!("Failed to get device collection: {}", e))?;
        let mut devices = Vec::new();

        for device in collection.into_iter() {
            if let Ok(dev) = device {
                if let Ok(name) = dev.get_friendlyname() {
                    devices.push(name);
                }
            }
        }

        Ok(devices)
    }
}

impl AudioOutput for UsbAudioOutput {
    fn write(&mut self, encoded_data: &[u8]) -> Result<()> {
        self.buffer.extend_from_slice(encoded_data);
        self.bytes_written += encoded_data.len() as u64;

        if self.buffer.len() >= 4096 {
            debug!("UsbAudioOutput: Buffered {} bytes total (experimental - data discarded)",
                   self.bytes_written);
            self.buffer.clear();
        }

        Ok(())
    }

    fn flush(&mut self) -> Result<()> {
        if !self.buffer.is_empty() {
            debug!("UsbAudioOutput: Flushing {} bytes", self.buffer.len());
            self.buffer.clear();
        }
        Ok(())
    }

    fn close(&mut self) -> Result<()> {
        self.flush()?;
        info!("UsbAudioOutput: Closed, {} bytes written (experimental)", self.bytes_written);
        Ok(())
    }
}
