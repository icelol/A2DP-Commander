fn main() {
    build_openaptx();
    build_libldac();
}

fn build_openaptx() {
    println!("cargo:rerun-if-changed=vendor/libopenaptx/openaptx.c");
    println!("cargo:rerun-if-changed=vendor/libopenaptx/openaptx.h");

    cc::Build::new()
        .file("vendor/libopenaptx/openaptx.c")
        .include("vendor/libopenaptx")
        .opt_level(3)
        .compile("openaptx");

    println!("cargo:rustc-link-lib=static=openaptx");
}

fn build_libldac() {
    println!("cargo:rerun-if-changed=vendor/libldac/src");
    println!("cargo:rerun-if-changed=vendor/libldac/inc");

    cc::Build::new()
        .file("vendor/libldac/src/ldac_all.c")
        .include("vendor/libldac/inc")
        .include("vendor/libldac/src")
        .opt_level(3)
        .define("_USE_MATH_DEFINES", None)
        .compile("ldac");

    println!("cargo:rustc-link-lib=static=ldac");
}
