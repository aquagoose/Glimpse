cmake_minimum_required(VERSION 4.0)
project(Glimpse LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

set(GLP_SRC "")

file(GLOB_RECURSE GLP_AUDIO_SRC CONFIGURE_DEPENDS include/audio/*.h src/audio/*.h src/audio/*.c)
list(APPEND GLP_SRC ${GLP_AUDIO_SRC})

add_library(GlimpseNative SHARED ${GLP_SRC})

add_subdirectory(lib/mixr)
target_link_libraries(GlimpseNative PUBLIC slant)