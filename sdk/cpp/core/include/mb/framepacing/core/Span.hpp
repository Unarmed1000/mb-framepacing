#ifndef MB_FRAMEPACING_CORE_SPAN_HPP
#define MB_FRAMEPACING_CORE_SPAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The span every buffer of the SDK's API is: a view of contiguous elements the caller owns. In a library built for C++20 it is
// std::span itself. In one built for C++17 (MB_FRAMEPACING_CXX_STANDARD 17), which has no std::span, it is the class below: the part
// of std::span the SDK uses, with the same names and meaning, so that code written against it compiles either way. Which one it is
// follows from the library's build (mb/framepacing/core/Config.hpp), never from the standard the including file compiles with.

#include <mb/framepacing/core/Config.hpp>

#if MB_FRAMEPACING_CXX_STANDARD >= 20

#include <cstddef>
#include <span>

namespace MB::FramePacing
{
  //! The extent of a span whose size is known at run time only.
  inline constexpr std::size_t DynamicExtent = std::dynamic_extent;

  //! std::span (a library built for C++20).
  template <typename T, std::size_t TExtent = DynamicExtent>
  using Span = std::span<T, TExtent>;
}

#else

#include <array>
#include <cassert>
#include <cstddef>
#include <iterator>
#include <type_traits>

namespace MB::FramePacing
{
  //! The extent of a span whose size is known at run time only.
  inline constexpr std::size_t DynamicExtent = static_cast<std::size_t>(-1);

  //! A view of TExtent (or, with DynamicExtent, of a run-time count of) contiguous elements of type T: std::span's part that the SDK
  //! uses (a library built for C++17). A position or count outside the span is asserted, not checked, as std::span's.
  template <typename T, std::size_t TExtent = DynamicExtent>
  class Span
  {
    // std::span's rule: a U may be viewed as a T where an array of U converts to an array of T (adds const, never converts a type)
    template <typename U>
    // NOLINTNEXTLINE(modernize-avoid-c-arrays)
    static constexpr bool IsElementConvertible = std::is_convertible_v<U (*)[], T (*)[]>;

    template <typename TContainer>
    using ContainerElement = std::remove_pointer_t<decltype(std::data(std::declval<TContainer&>()))>;

    T* m_data{nullptr};
    std::size_t m_size{0};

  public:
    // The names are std::span's, so that code written against Span compiles in a C++20 build, where it is std::span
    // NOLINTBEGIN(readability-identifier-naming)
    using element_type = T;
    using value_type = std::remove_cv_t<T>;
    using size_type = std::size_t;
    using difference_type = std::ptrdiff_t;
    using pointer = T*;
    using const_pointer = const T*;
    using reference = T&;
    using const_reference = const T&;
    using iterator = T*;

    static constexpr std::size_t extent = TExtent;

    //! No elements (a span of a dynamic extent or of extent 0 only).
    template <std::size_t TDefaultExtent = TExtent, std::enable_if_t<TDefaultExtent == DynamicExtent || TDefaultExtent == 0, int> = 0>
    constexpr Span() noexcept
    {
    }

    //! The count elements from elements on.
    constexpr Span(T* const elements, const std::size_t count) noexcept
      : m_data(elements)
      , m_size(count)
    {
      assert(TExtent == DynamicExtent || count == TExtent);
    }

    // The conversions are implicit, as std::span's are: a function that takes a Span takes an array or a container as it is
    // NOLINTBEGIN(google-explicit-constructor)
    //! Every element of an array.
    // A C array is what this constructor takes, as std::span's does
    template <std::size_t TCount, std::enable_if_t<TExtent == DynamicExtent || TCount == TExtent, int> = 0>
    // NOLINTNEXTLINE(modernize-avoid-c-arrays)
    constexpr Span(T (&array)[TCount]) noexcept
      : m_data(array)
      , m_size(TCount)
    {
    }

    //! Every element of a std::array.
    template <typename U, std::size_t TCount, std::enable_if_t<(TExtent == DynamicExtent || TCount == TExtent) && IsElementConvertible<U>, int> = 0>
    constexpr Span(std::array<U, TCount>& array) noexcept
      : m_data(array.data())
      , m_size(TCount)
    {
    }

    //! Every element of a std::array (a span of const elements).
    template <typename U, std::size_t TCount,
              std::enable_if_t<(TExtent == DynamicExtent || TCount == TExtent) && IsElementConvertible<const U>, int> = 0>
    constexpr Span(const std::array<U, TCount>& array) noexcept
      : m_data(array.data())
      , m_size(TCount)
    {
    }

    //! Every element of a contiguous container with data() and size(): a std::vector, a std::string, a C++20 std::span (a span of
    //! a dynamic extent only).
    template <typename TContainer, std::enable_if_t<TExtent == DynamicExtent && !std::is_array_v<std::remove_cv_t<TContainer>> &&
                                                      IsElementConvertible<ContainerElement<TContainer>> &&
                                                      std::is_convertible_v<decltype(std::size(std::declval<TContainer&>())), std::size_t>,
                                                    int> = 0>
    constexpr Span(TContainer& container) noexcept
      : m_data(std::data(container))
      , m_size(std::size(container))
    {
    }

    //! Every element of a contiguous container (a span of const elements: the container may be a temporary).
    template <typename TContainer,
              std::enable_if_t<TExtent == DynamicExtent && !std::is_array_v<TContainer> && IsElementConvertible<ContainerElement<const TContainer>> &&
                                 std::is_convertible_v<decltype(std::size(std::declval<const TContainer&>())), std::size_t>,
                               int> = 0>
    constexpr Span(const TContainer& container) noexcept
      : m_data(std::data(container))
      , m_size(std::size(container))
    {
    }

    //! The elements of another span: one of non-const elements as const ones, one of a fixed extent as a dynamic one.
    template <typename U, std::size_t TCount, std::enable_if_t<(TExtent == DynamicExtent || TCount == TExtent) && IsElementConvertible<U>, int> = 0>
    constexpr Span(const Span<U, TCount>& other) noexcept
      : m_data(other.data())
      , m_size(other.size())
    {
    }

    // NOLINTEND(google-explicit-constructor)

    [[nodiscard]] constexpr T* data() const noexcept
    {
      return m_data;
    }

    [[nodiscard]] constexpr std::size_t size() const noexcept
    {
      return m_size;
    }

    [[nodiscard]] constexpr std::size_t size_bytes() const noexcept
    {
      return m_size * sizeof(T);
    }

    [[nodiscard]] constexpr bool empty() const noexcept
    {
      return m_size == 0;
    }

    [[nodiscard]] constexpr T& operator[](const std::size_t index) const noexcept
    {
      assert(index < m_size);
      return m_data[index];
    }

    [[nodiscard]] constexpr T* begin() const noexcept
    {
      return m_data;
    }

    [[nodiscard]] constexpr T* end() const noexcept
    {
      return m_data + m_size;
    }

    //! The first count elements.
    [[nodiscard]] constexpr Span<T> first(const std::size_t count) const noexcept
    {
      assert(count <= m_size);
      return {m_data, count};
    }

    //! The first TFirst elements, as a span of that extent.
    template <std::size_t TFirst>
    [[nodiscard]] constexpr Span<T, TFirst> first() const noexcept
    {
      assert(TFirst <= m_size);
      return Span<T, TFirst>(m_data, TFirst);
    }

    //! The last count elements.
    [[nodiscard]] constexpr Span<T> last(const std::size_t count) const noexcept
    {
      assert(count <= m_size);
      return {m_data + (m_size - count), count};
    }

    //! The count elements from offset on; with DynamicExtent, every element from offset on.
    [[nodiscard]] constexpr Span<T> subspan(const std::size_t offset, const std::size_t count = DynamicExtent) const noexcept
    {
      assert(offset <= m_size);
      assert(count == DynamicExtent || count <= m_size - offset);
      return {m_data + offset, count == DynamicExtent ? m_size - offset : count};
    }
    // NOLINTEND(readability-identifier-naming)
  };
}

#endif

#endif
