/*
 * Combined LDAC sources for single-unit compilation
 * This works around DECLFUNC static issue on MSVC
 *
 * We include individual source files directly (not ldaclib.c/ldacBT.c)
 * to avoid duplicate definitions from nested includes.
 */

#ifdef _MSC_VER
#define __attribute__(x)
#endif

#include "ldac.h"
#include "ldacBT.h"

/* Core encoder tables and utilities */
#include "tables_ldac.c"
#include "tables_sigproc_ldac.c"
#include "memory_ldac.c"
#include "setpcm_ldac.c"

/* Encoder implementation */
#include "mdct_ldac.c"
#include "sigana_ldac.c"
#include "bitalloc_sub_ldac.c"
#include "bitalloc_ldac.c"
#include "quant_ldac.c"
#include "pack_ldac.c"
#include "encode_ldac.c"

/* LDAC library API */
#include "ldaclib_api.c"

/* Bluetooth API */
#include "ldacBT_internal.c"
#include "ldacBT_api.c"
