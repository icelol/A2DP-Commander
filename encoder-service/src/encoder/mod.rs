mod aptx;
mod aptx_ffi;
mod ldac;
mod ldac_ffi;
mod sbc;

pub use aptx::AptxEncoder;
pub use ldac::LdacEncoder;
pub use sbc::SbcEncoder;

use anyhow::Result;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Codec {
    Sbc,
    Ldac,
    Aptx,
    AptxHd,
}

impl std::fmt::Display for Codec {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Codec::Sbc => write!(f, "SBC"),
            Codec::Ldac => write!(f, "LDAC"),
            Codec::Aptx => write!(f, "aptX"),
            Codec::AptxHd => write!(f, "aptX HD"),
        }
    }
}

#[derive(Debug, Clone, Copy)]
pub enum LdacQuality {
    High,   // 990 kbps
    Standard, // 660 kbps
    Mobile,  // 330 kbps
}

impl LdacQuality {
    pub fn bitrate(&self) -> u32 {
        match self {
            LdacQuality::High => 990000,
            LdacQuality::Standard => 660000,
            LdacQuality::Mobile => 330000,
        }
    }
}

pub trait AudioEncoder: Send {
    fn codec(&self) -> Codec;
    fn encode(&mut self, pcm: &[i16]) -> Result<Vec<u8>>;
    fn frame_size(&self) -> usize;
    fn bitrate(&self) -> u32;
}

#[derive(Debug, Clone)]
pub struct EncodedFrame {
    pub codec: Codec,
    pub data: Vec<u8>,
    pub timestamp: u64,
    pub duration_ms: f64,
}
