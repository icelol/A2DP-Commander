mod file;
mod usb_audio;

pub use file::FileOutput;
pub use usb_audio::UsbAudioOutput;

use anyhow::Result;

pub trait AudioOutput: Send {
    fn write(&mut self, encoded_data: &[u8]) -> Result<()>;
    fn flush(&mut self) -> Result<()>;
    fn close(&mut self) -> Result<()>;
}

#[derive(Debug, Clone)]
pub enum OutputTarget {
    File { path: String },
    UsbAudio { device_name: Option<String> },
    Null,
}

impl Default for OutputTarget {
    fn default() -> Self {
        OutputTarget::Null
    }
}

pub struct NullOutput;

impl AudioOutput for NullOutput {
    fn write(&mut self, _encoded_data: &[u8]) -> Result<()> {
        Ok(())
    }

    fn flush(&mut self) -> Result<()> {
        Ok(())
    }

    fn close(&mut self) -> Result<()> {
        Ok(())
    }
}

pub fn create_output(target: &OutputTarget) -> Result<Box<dyn AudioOutput>> {
    match target {
        OutputTarget::File { path } => Ok(Box::new(FileOutput::new(path)?)),
        OutputTarget::UsbAudio { device_name } => Ok(Box::new(UsbAudioOutput::new(device_name.clone())?)),
        OutputTarget::Null => Ok(Box::new(NullOutput)),
    }
}
