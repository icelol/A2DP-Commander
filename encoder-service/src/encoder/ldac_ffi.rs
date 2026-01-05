use std::ffi::c_int;

#[repr(C)]
pub struct ldac_bt_handle {
    _opaque: [u8; 0],
}

pub type HANDLE_LDAC_BT = *mut ldac_bt_handle;

pub const LDAC_CCI_MONO: c_int = 0;
pub const LDAC_CCI_DUAL_CHANNEL: c_int = 1;
pub const LDAC_CCI_STEREO: c_int = 2;

pub const LDACBT_SMPL_FMT_S16: c_int = 0x2;
pub const LDACBT_SMPL_FMT_S24: c_int = 0x3;
pub const LDACBT_SMPL_FMT_S32: c_int = 0x4;
pub const LDACBT_SMPL_FMT_F32: c_int = 0x5;

pub const LDACBT_EQMID_HQ: c_int = 0;
pub const LDACBT_EQMID_SQ: c_int = 1;
pub const LDACBT_EQMID_MQ: c_int = 2;

pub const LDACBT_ENC_LSU: c_int = 128;
pub const LDACBT_MAX_NBYTES: c_int = 1024;

pub const LDACBT_ERR_NONE: c_int = 0;
pub const LDACBT_ERR_FATAL: c_int = 256;

extern "C" {
    pub fn ldacBT_get_handle() -> HANDLE_LDAC_BT;
    pub fn ldacBT_free_handle(h: HANDLE_LDAC_BT);
    pub fn ldacBT_close_handle(h: HANDLE_LDAC_BT);
    pub fn ldacBT_get_version() -> c_int;

    pub fn ldacBT_init_handle_encode(
        h: HANDLE_LDAC_BT,
        mtu: c_int,
        eqmid: c_int,
        cm: c_int,
        fmt: c_int,
        sf: c_int,
    ) -> c_int;

    pub fn ldacBT_encode(
        h: HANDLE_LDAC_BT,
        p_pcm: *const u8,
        pcm_used: *mut c_int,
        p_stream: *mut u8,
        stream_sz: *mut c_int,
        frame_num: *mut c_int,
    ) -> c_int;

    pub fn ldacBT_set_eqmid(h: HANDLE_LDAC_BT, eqmid: c_int) -> c_int;
    pub fn ldacBT_get_eqmid(h: HANDLE_LDAC_BT) -> c_int;
    pub fn ldacBT_get_bitrate(h: HANDLE_LDAC_BT) -> c_int;
    pub fn ldacBT_get_sampling_freq(h: HANDLE_LDAC_BT) -> c_int;
    pub fn ldacBT_get_error_code(h: HANDLE_LDAC_BT) -> c_int;
}
