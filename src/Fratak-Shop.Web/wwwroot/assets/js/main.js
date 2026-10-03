//////////////////////////////// Quiantity Input
function quantityDecrement(e) {
  const btn = e.target.parentNode.parentElement.querySelector(
    'button[data-action="increment"]'
  );
  const target = btn.nextElementSibling;
  let value = Number(target.value);
  if (value == 1) return;
  value--;
  target.value = value;
}

function quantityIncrement(e) {
  const btn = e.target.parentNode.parentElement.querySelector(
    'button[data-action="increment"]'
  );
  const target = btn.nextElementSibling;
  let value = Number(target.value);
  value++;
  target.value = value;
}

const quantityDecrementButtons = document.querySelectorAll(
  `button[data-action="decrement"]`
);

const quantityIncrementButtons = document.querySelectorAll(
  `button[data-action="increment"]`
);

quantityDecrementButtons.forEach((btn) => {
  btn.addEventListener("click", quantityDecrement);
});

quantityIncrementButtons.forEach((btn) => {
  btn.addEventListener("click", quantityIncrement);
});

/////////////////////////////////////////////// profile edit address modal
function showCard() {
  let showCard = document.getElementById("cardHeader");
  showCard.classList.remove("hidden");
}
function closeCard() {
  let showCard = document.getElementById("cardHeader");
  showCard.classList.add("hidden");
}

////////////////////////////////////////////// open hover menu header
function showNavbarMobile() {
  let showNavbarMobile = document.getElementById("navbarMobile");
  showNavbarMobile.classList.remove("-right-full");
  showNavbarMobile.classList.add("right-0");
  showNavbarMobile.classList.remove("hidden");
}
function hideNavbarMobile() {
  let hideNavbarMobile = document.getElementById("navbarMobile");
  hideNavbarMobile.classList.add("-right-full");
  hideNavbarMobile.classList.add("hidden");
}
//////////////////////////////////////// off slider
(function () {
  const second = 1000,
    minute = second * 60,
    hour = minute * 60,
    day = hour * 24;

  //I'm adding this section so I don't have to keep updating this pen every year :-)
  //remove this if you don't need it
  let today = new Date(),
    yyyy = today.getFullYear(),
    dayMonth = "12/29/",
    birthday = dayMonth + yyyy;

  const countDown = new Date(birthday).getTime(),
    x = setInterval(function () {
      const now = new Date().getTime(),
        distance = countDown - now;
      // (document.getElementById("days").innerText = Math.floor(distance / day)),
      for (let i = 0; i < document.querySelectorAll("#days").length; i++) {
        document.querySelectorAll("#days")[i].innerText = Math.floor(
          distance / day
        );
        document.querySelectorAll("#hours")[i].innerText = Math.floor(
          (distance % day) / hour
        );
        document.querySelectorAll("#minutes")[i].innerText = Math.floor(
          (distance % hour) / minute
        );
        document.querySelectorAll("#seconds")[i].innerText = Math.floor(
          (distance % minute) / second
        );
      }
    }, 1000);
})();

////////////////////////////////// btn go to up
let mybutton = document.getElementById("btn-back-to-top");
function backToTop() {
  document.body.scrollTop = 0;
  document.documentElement.scrollTop = 0;
}
/////////////////////////////////////////////// profile edit address modal
function showEditAddress() {
  let showEditAddress = document.getElementById("showEditAddress");
  showEditAddress.classList.remove("hidden");
}
function closeEditAddress() {
  let showEditAddress = document.getElementById("showEditAddress");
  showEditAddress.classList.add("hidden");
}
//////////////////////////////////// Verify 4 Code

const button = document.querySelector("button");
let arrayInputs = [] ;
arrayInputs = document.querySelectorAll(".inputVerify");
arrayInputs.forEach((input, index1) => {
input.addEventListener("keyup", (e) => {
  const currentInput = input,
    nextInput = input.nextElementSibling,
    prevInput = input.previousElementSibling;
  if (currentInput.value.length > 1) {
    currentInput.value = "";
    return;
  }
  if (
    nextInput &&
    nextInput.hasAttribute("disabled") &&
    currentInput.value !== ""
  ) {
    nextInput.removeAttribute("disabled");
    nextInput.focus();
  }
  if (e.key === "Backspace") {
    arrayInputs.forEach((input, index2) => {
      if (index1 <= index2 && prevInput) {
        input.setAttribute("disabled", true);
        input.value = "";
        prevInput.focus();
      }
    });
  }
  if (!arrayInputs[3].disabled && arrayInputs[3].value !== "") {
    button.classList.add("active");
    return;
  }
  button.classList.remove("active");
});
});
window.addEventListener("load", () => arrayInputs[0].focus());
///////////////////////////////////////////////// verify resend code
var minutes = 0;
var seconds = 60;

var x = setInterval(function () {
  if (seconds != 0) {
    seconds--;
  } else if (seconds == 0) {
    minutes--;
    seconds = 59;
  }
  if (document.getElementById("verify-code")) {
    document.getElementById("verify-code").innerHTML = "ارسال مجدد کد بعد از " + "ثانیه " + seconds;
  }
  if (seconds == 0 && minutes == 0) {
    clearInterval(x);
    document.getElementById("block-verify-code").innerHTML =
      "<a href='#'>ارسال مجدد</a>";
  }
}, 1000);


/////////////////////////////////////////// product comparision show select product
function showSelectProduct() {
  let showSelectP = document.getElementById("showSelectP");
  showSelectP.classList.remove("hidden");
}
function closeSelectP() {
  let showSelectP = document.getElementById("showSelectP");
  showSelectP.classList.add("hidden");
}
/////////////////////////////////////////// product images modal
function showImagesProduct() {
  let showImagesP = document.getElementById("showImagesModal");
  showImagesP.classList.remove("hidden");
}
function closeImagesProduct() {
  let showImagesP = document.getElementById("showImagesModal");
  showImagesP.classList.add("hidden");
}
/////////////////////////////////////////// slide show single product
let slideIndex = 1;
showSlides(slideIndex);

function plusSlides(n) {
  showSlides((slideIndex += n));
}

function currentSlide(n) {
  showSlides((slideIndex = n));
}

function showSlides(n) {
  let i;
  let slides = document.getElementsByClassName("mySlides");
  let dots = document.getElementsByClassName("demo");
  if (n > slides.length) {
    slideIndex = 1;
  }
  if (n < 1) {
    slideIndex = slides.length;
  }
  for (i = 0; i < slides.length; i++) {
    slides[i].style.display = "none";
  }
  for (i = 0; i < dots.length; i++) {
    dots[i].className = dots[i].className.replace(" active", "");
  }
  slides[slideIndex - 1].style.display = "block";
  dots[slideIndex - 1].className += " active";
}