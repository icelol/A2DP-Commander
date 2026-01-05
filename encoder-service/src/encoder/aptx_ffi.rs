use std::ffi::c_int;

#[repr(C)]
pub struct aptx_context {
    _opaque: [u8; 0],
}

pub type AptxContext = aptx_context;

extern "C" {
    pub fn aptx_init(hd: c_int) -> *mut aptx_context;
    pub fn aptx_reset(ctx: *mut aptx_context);
    pub fn aptx_finish(ctx: *mut aptx_context);
    pub fn aptx_encode(
        ctx: *mut aptx_context,
        input: *const u8,
        input_size: usize,
        output: *mut u8,
        output_size: usize,
        written: *mut usize,
    ) -> usize;
    pub fn aptx_encode_finish(
        ctx: *mut aptx_context,
        output: *mut u8,
        output_size: usize,
        written: *mut usize,
    ) -> c_int;
}
