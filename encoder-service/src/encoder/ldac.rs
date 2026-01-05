use anyhow::{anyhow, Result};
use tracing::{debug, error, info};

use super::ldac_ffi::*;
use super::{AudioEncoder, Codec, LdacQuality};

const LDAC_FRAME_SAMPLES: usize = 128;
const LDAC_MTU: i32 = 679;

pub struct LdacEncoder {
    handle: HANDLE_LDAC_BT,
    quality: LdacQuality,
    sample_rate: u32,
    frame_buffer: Vec<i16>,
}

impl LdacEncoder {
    pub fn new(sample_rate: u32, channels: u16, quality: LdacQuality) -> Result<Self> {
        if channels != 2 {
            return Err(anyhow!("LDAC requires stereo input"));
        }

        if ![44100, 48000, 88200, 96000].contains(&sample_rate) {
            return Err(anyhow!(
                "LDAC requires 44100, 48000, 88200 or 96000 Hz, got {}",
                sample_rate
            ));
        }

        let handle = unsafe { ldacBT_get_handle() };
        if handle.is_null() {
            return Err(anyhow!("Failed to allocate LDAC handle"));
        }

        let eqmid = match quality {
            LdacQuality::High => LDACBT_EQMID_HQ,
            LdacQuality::Standard => LDACBT_EQMID_SQ,
            LdacQuality::Mobile => LDACBT_EQMID_MQ,
        };

        let ret = unsafe {
            ldacBT_init_handle_encode(
                handle,
                LDAC_MTU,
                eqmid,
                LDAC_CCI_STEREO,
                LDACBT_SMPL_FMT_S16,
                sample_rate as i32,
            )
        };

        if ret != 0 {
            let err = unsafe { ldacBT_get_error_code(handle) };
            unsafe { ldacBT_free_handle(handle) };
            return Err(anyhow!("Failed to init LDAC encoder: error {}", err));
        }

        let version = unsafe { ldacBT_get_version() };
        info!(
            "LDAC encoder v{}.{}.{}: {} Hz, {} kbps",
            (version >> 16) & 0xFF,
            (version >> 8) & 0xFF,
            version & 0xFF,
            sample_rate,
            quality.bitrate() / 1000
        );

        Ok(Self {
            handle,
            quality,
            sample_rate,
            frame_buffer: Vec::with_capacity(LDAC_FRAME_SAMPLES * 2 * 2),
        })
    }

    pub fn set_quality(&mut self, quality: LdacQuality) -> Result<()> {
        let eqmid = match quality {
            LdacQuality::High => LDACBT_EQMID_HQ,
            LdacQuality::Standard => LDACBT_EQMID_SQ,
            LdacQuality::Mobile => LDACBT_EQMID_MQ,
        };

        let ret = unsafe { ldacBT_set_eqmid(self.handle, eqmid) };
        if ret != 0 {
            let err = unsafe { ldacBT_get_error_code(self.handle) };
            return Err(anyhow!("Failed to set LDAC quality: error {}", err));
        }

        self.quality = quality;
        debug!("LDAC quality set to {} kbps", quality.bitrate() / 1000);
        Ok(())
    }
}

impl Drop for LdacEncoder {
    fn drop(&mut self) {
        if !self.handle.is_null() {
            unsafe {
                ldacBT_close_handle(self.handle);
                ldacBT_free_handle(self.handle);
            }
            debug!("LDAC encoder destroyed");
        }
    }
}

impl AudioEncoder for LdacEncoder {
    fn codec(&self) -> Codec {
        Codec::Ldac
    }

    fn encode(&mut self, pcm: &[i16]) -> Result<Vec<u8>> {
        self.frame_buffer.extend_from_slice(pcm);

        let frame_samples = LDAC_FRAME_SAMPLES * 2;
        let mut output = Vec::new();

        while self.frame_buffer.len() >= frame_samples {
            let frame: Vec<i16> = self.frame_buffer.drain(..frame_samples).collect();

            let input_ptr = frame.as_ptr() as *const u8;
            let mut pcm_used: i32 = 0;
            let mut stream_buf = vec![0u8; LDACBT_MAX_NBYTES as usize];
            let mut stream_sz: i32 = 0;
            let mut frame_num: i32 = 0;

            let ret = unsafe {
                ldacBT_encode(
                    self.handle,
                    input_ptr,
                    &mut pcm_used,
                    stream_buf.as_mut_ptr(),
                    &mut stream_sz,
                    &mut frame_num,
                )
            };

            if ret != 0 {
                let err = unsafe { ldacBT_get_error_code(self.handle) };
                if err >= LDACBT_ERR_FATAL {
                    error!("LDAC fatal encode error: {}", err);
                    return Err(anyhow!("LDAC encode failed: error {}", err));
                }
            }

            if stream_sz > 0 {
                stream_buf.truncate(stream_sz as usize);
                output.extend(stream_buf);
            }
        }

        Ok(output)
    }

    fn frame_size(&self) -> usize {
        LDAC_FRAME_SAMPLES * 2
    }

    fn bitrate(&self) -> u32 {
        let br = unsafe { ldacBT_get_bitrate(self.handle) };
        if br > 0 {
            br as u32
        } else {
            self.quality.bitrate()
        }
    }
}

unsafe impl Send for LdacEncoder {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_ldac_encoder_init() {
        let encoder = LdacEncoder::new(48000, 2, LdacQuality::High);
        assert!(encoder.is_ok(), "Failed to create LDAC encoder: {:?}", encoder.err());
        let encoder = encoder.unwrap();
        assert_eq!(encoder.codec(), Codec::Ldac);
    }

    #[test]
    fn test_ldac_encode_silence() {
        let mut encoder = LdacEncoder::new(48000, 2, LdacQuality::High).unwrap();
        let silence: Vec<i16> = vec![0; 256 * 2];
        let result = encoder.encode(&silence);
        assert!(result.is_ok(), "Encode failed: {:?}", result.err());
        let encoded = result.unwrap();
        assert!(!encoded.is_empty(), "Encoded data should not be empty");
        println!("LDAC encoded {} samples to {} bytes", silence.len() / 2, encoded.len());
    }

    #[test]
    fn test_ldac_encode_sine() {
        let mut encoder = LdacEncoder::new(48000, 2, LdacQuality::High).unwrap();
        let mut samples = Vec::new();
        for i in 0..512 {
            let t = i as f32 / 48000.0;
            let sample = (f32::sin(2.0 * std::f32::consts::PI * 440.0 * t) * 16000.0) as i16;
            samples.push(sample);
            samples.push(sample);
        }
        let result = encoder.encode(&samples);
        assert!(result.is_ok(), "Encode failed: {:?}", result.err());
        let encoded = result.unwrap();
        assert!(!encoded.is_empty(), "Encoded data should not be empty");
        println!("LDAC encoded {} stereo samples to {} bytes", samples.len() / 2, encoded.len());
    }
}
