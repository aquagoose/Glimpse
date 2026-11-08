cmake_minimum_required(VERSION 4.0)
project(Glimpse LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

set(GLP_SRC "")

file(GLOB_RECURSE GLP_AUDIO_SRC CONFIGURE_DEPENDS include/audio/*.h src/audio/*.h src/audio/*.c)
list(APPEND GLP_SRC ${GLP_AUDIO_SRC})

add_library(GlimpseNative SHARED ${GLP_SRC})
target_include_directories(GlimpseNative PUBLIC include)

add_subdirectory(lib/mixr)
target_link_libraries(GlimpseNative PUBLIC slant)

# glimpse still ships its own SDL3 binaries for the moment, so we can just use those.
find_package(SDL3 REQUIRED)
target_link_libraries(GlimpseNative PUBLIC SDL3::SDL3)

function(glp_test name source)
    add_executable(${name} ${source})
    target_link_libraries(${name} PRIVATE GlimpseNative)
endfunction()

glp_test(PlayTrack tests/PlayTrack.c)