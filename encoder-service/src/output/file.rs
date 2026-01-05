use anyhow::Result;
use std::fs::File;
use std::io::{BufWriter, Write};
use tracing::info;

use super::AudioOutput;

pub struct FileOutput {
    writer: BufWriter<File>,
    path: String,
    bytes_written: u64,
}

impl FileOutput {
    pub fn new(path: &str) -> Result<Self> {
        let file = File::create(path)?;
        info!("FileOutput: Writing to {}", path);
        Ok(Self {
            writer: BufWriter::with_capacity(64 * 1024, file),
            path: path.to_string(),
            bytes_written: 0,
        })
    }
}

impl AudioOutput for FileOutput {
    fn write(&mut self, encoded_data: &[u8]) -> Result<()> {
        self.writer.write_all(encoded_data)?;
        self.bytes_written += encoded_data.len() as u64;
        Ok(())
    }

    fn flush(&mut self) -> Result<()> {
        self.writer.flush()?;
        Ok(())
    }

    fn close(&mut self) -> Result<()> {
        self.flush()?;
        info!("FileOutput: Closed {}, wrote {} bytes", self.path, self.bytes_written);
        Ok(())
    }
}

impl Drop for FileOutput {
    fn drop(&mut self) {
        let _ = self.close();
    }
}
